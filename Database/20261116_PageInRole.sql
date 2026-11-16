-- Run in the same database used by the SchoolManagement connection string.
IF OBJECT_ID(N'dbo.PageInRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PageInRole
    (
        UserId INT NOT NULL,
        PathId INT NOT NULL,
        DesignationId TINYINT NOT NULL,
        IsAdd BIT NOT NULL DEFAULT (0),
        IsEdit BIT NOT NULL DEFAULT (0),
        IsRead BIT NOT NULL DEFAULT (0),
        IsDelete BIT NOT NULL DEFAULT (0),
        IsImport BIT NOT NULL DEFAULT (0),
        IsExport BIT NOT NULL DEFAULT (0),
        Approve1 BIT NOT NULL DEFAULT (0),
        Approve2 BIT NOT NULL DEFAULT (0),
        [Type] INT NOT NULL,
        [RowVersion] ROWVERSION NOT NULL,
        UniqueKey UNIQUEIDENTIFIER NOT NULL,
        LastLogNo BIGINT NOT NULL,
        createby INT NULL,
        createdt DATETIME NULL,
        Lmodifyby INT NULL,
        Lmodifydt DATETIME NULL,
        CONSTRAINT UQ_RolePagePermission_Role_Page UNIQUE (UserId, PathId, DesignationId),
        CONSTRAINT CK_PageInRole_Target CHECK
        (
            ([Type] = 1 AND UserId > 0 AND DesignationId = 0)
            OR ([Type] = 2 AND UserId = 0 AND DesignationId > 0)
        )
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PageInRole_Targets
    @Type INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @Type = 1
    BEGIN
        SELECT u.slno AS Id,
               CONVERT(NVARCHAR(600), CONCAT(
                   COALESCE(NULLIF(LTRIM(RTRIM(u.FullName)), N''), u.UserName, u.Email),
                   CASE WHEN u.Email IS NOT NULL AND u.Email <> ISNULL(u.FullName, u.UserName)
                        THEN CONCAT(N' (', u.Email, N')') ELSE N'' END)) AS [Name]
        FROM dbo.tblUsers AS u
        WHERE u.IsActive = 1 AND u.deludt IS NULL AND u.slno > 0
        ORDER BY [Name], Id;
    END
    ELSE
    BEGIN
        SELECT d.DesignationId AS Id, CONVERT(NVARCHAR(600), d.DesignationName) AS [Name]
        FROM dbo.tblDesignations AS d
        WHERE @Type = 2 AND d.IsActive = 1 AND d.deludt IS NULL
          AND d.DesignationId > 0
          AND (COL_LENGTH(N'dbo.PageInRole', N'DesignationId') > 1 OR d.DesignationId <= 255)
        ORDER BY [Name], Id;
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PageInRole_List
    @Type INT,
    @TargetId INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @Type NOT IN (1, 2) OR @TargetId <= 0
        THROW 51040, 'Choose a valid user or designation.', 1;

    DECLARE @UserId INT = CASE WHEN @Type = 1 THEN @TargetId ELSE 0 END,
            @DesignationId INT = CASE WHEN @Type = 2 THEN @TargetId ELSE 0 END;

    SELECT m.PathId, ISNULL(m.HierarchyId, 0) AS HierarchyId, m.PageName, m.IsLeafNode,
           CONVERT(VARCHAR(16), r.[RowVersion], 2) AS [RowVersion],
           p.IsAdd AS CanAdd, p.IsEdit AS CanEdit, p.IsRead AS CanRead, p.IsDelete AS CanDelete,
           p.IsImport AS CanImport, p.IsExport AS CanExport,
           ISNULL(p.[Approve 1], 0) AS CanApprove1, ISNULL(p.[approve 2], 0) AS CanApprove2,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsAdd = 1 THEN ISNULL(r.IsAdd, 0) ELSE 0 END) AS IsAdd,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsEdit = 1 THEN ISNULL(r.IsEdit, 0) ELSE 0 END) AS IsEdit,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsRead = 1 THEN ISNULL(r.IsRead, 0) ELSE 0 END) AS IsRead,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsDelete = 1 THEN ISNULL(r.IsDelete, 0) ELSE 0 END) AS IsDelete,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsImport = 1 THEN ISNULL(r.IsImport, 0) ELSE 0 END) AS IsImport,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsExport = 1 THEN ISNULL(r.IsExport, 0) ELSE 0 END) AS IsExport,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.[Approve 1] = 1 THEN ISNULL(r.Approve1, 0) ELSE 0 END) AS Approve1,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.[approve 2] = 1 THEN ISNULL(r.Approve2, 0) ELSE 0 END) AS Approve2
    FROM dbo.ModulePath AS m
    INNER JOIN dbo.PageSetting AS p ON p.PageSettingId = m.PageSettingId
    LEFT JOIN dbo.PageInRole AS r ON r.PathId = m.PathId AND r.[Type] = @Type
        AND r.UserId = @UserId AND r.DesignationId = @DesignationId
    WHERE m.ApplicationId = 1 AND m.IsActive = 1 AND m.deludt IS NULL AND p.DeletedDt IS NULL
    ORDER BY m.SequenceId, m.PageName, m.PathId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PageInRole_Save
    @Type INT,
    @TargetId INT,
    @PathId INT,
    @RowVersion VARCHAR(16),
    @IsAdd BIT,
    @IsEdit BIT,
    @IsRead BIT,
    @IsDelete BIT,
    @IsImport BIT,
    @IsExport BIT,
    @Approve1 BIT,
    @Approve2 BIT,
    @ActorId NVARCHAR(450),
    @SessionId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Type NOT IN (1, 2) OR @TargetId <= 0
        THROW 51040, 'Choose a valid user or designation.', 1;

    DECLARE @UserId INT = CASE WHEN @Type = 1 THEN @TargetId ELSE 0 END,
            @DesignationId INT = CASE WHEN @Type = 2 THEN @TargetId ELSE 0 END,
            @ActorSerial INT, @PageSettingId INT, @CurrentVersion BINARY(8), @CurrentType INT,
            @LastLogNo BIGINT = 0, @Now DATETIME = GETUTCDATE();
    DECLARE @ExpectedVersion BINARY(8) = TRY_CONVERT(BINARY(8), @RowVersion, 2);
    DECLARE @Saved TABLE ([RowVersion] BINARY(8));

    BEGIN TRANSACTION;

    SELECT @ActorSerial = slno
    FROM dbo.tblUsers WITH (HOLDLOCK)
    WHERE Id = @ActorId AND IsActive = 1 AND deludt IS NULL;

    IF ISNULL(@ActorSerial, 0) <= 0
        THROW 51040, 'The signed-in user does not have a valid serial number.', 1;

    IF (@Type = 1 AND NOT EXISTS
        (SELECT 1 FROM dbo.tblUsers WITH (HOLDLOCK)
         WHERE slno = @UserId AND IsActive = 1 AND deludt IS NULL))
       OR (@Type = 2 AND
           ((COL_LENGTH(N'dbo.PageInRole', N'DesignationId') = 1 AND @DesignationId > 255)
            OR NOT EXISTS
               (SELECT 1 FROM dbo.tblDesignations WITH (HOLDLOCK)
                WHERE DesignationId = @DesignationId AND IsActive = 1 AND deludt IS NULL)))
        THROW 51040, 'The selected user or designation is unavailable.', 1;

    SELECT @PageSettingId = m.PageSettingId
    FROM dbo.ModulePath AS m WITH (HOLDLOCK)
    INNER JOIN dbo.PageSetting AS p WITH (HOLDLOCK) ON p.PageSettingId = m.PageSettingId
    WHERE m.PathId = @PathId AND m.ApplicationId = 1 AND m.IsActive = 1
      AND m.IsLeafNode = 1 AND m.deludt IS NULL AND p.DeletedDt IS NULL;

    IF @PageSettingId IS NULL
        THROW 51041, 'The selected page is unavailable or is a folder.', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.PageSetting
        WHERE PageSettingId = @PageSettingId
          AND ((@IsAdd = 1 AND IsAdd = 0) OR (@IsEdit = 1 AND IsEdit = 0)
            OR (@IsRead = 1 AND IsRead = 0) OR (@IsDelete = 1 AND IsDelete = 0)
            OR (@IsImport = 1 AND IsImport = 0) OR (@IsExport = 1 AND IsExport = 0)
            OR (@Approve1 = 1 AND ISNULL([Approve 1], 0) = 0)
            OR (@Approve2 = 1 AND ISNULL([approve 2], 0) = 0))
    )
        THROW 51042, 'One or more rights are disabled in Page Setting. Search again before editing.', 1;

    SELECT @CurrentVersion = [RowVersion], @CurrentType = [Type]
    FROM dbo.PageInRole WITH (UPDLOCK, HOLDLOCK)
    WHERE UserId = @UserId AND PathId = @PathId AND DesignationId = @DesignationId;

    IF (@RowVersion IS NOT NULL AND (@ExpectedVersion IS NULL OR LEN(@RowVersion) <> 16))
       OR (@CurrentVersion IS NULL AND @RowVersion IS NOT NULL)
       OR (@CurrentVersion IS NOT NULL AND
           (@ExpectedVersion IS NULL OR @CurrentVersion <> @ExpectedVersion OR @CurrentType <> @Type))
        THROW 51043, 'These rights changed after the search. Refresh the page and try again.', 1;

    SELECT @LastLogNo = Id FROM dbo.tblUserLoginHistory WHERE SessionId = @SessionId AND UserId = @ActorId;

    IF @CurrentVersion IS NULL
    BEGIN
        INSERT dbo.PageInRole
        (
            UserId, PathId, DesignationId, IsAdd, IsEdit, IsRead, IsDelete, IsImport, IsExport,
            Approve1, Approve2, [Type], UniqueKey, LastLogNo, createby, createdt
        )
        OUTPUT INSERTED.[RowVersion] INTO @Saved
        VALUES
        (
            @UserId, @PathId, @DesignationId, @IsAdd, @IsEdit, @IsRead, @IsDelete, @IsImport, @IsExport,
            @Approve1, @Approve2, @Type, NEWID(), @LastLogNo, @ActorSerial, @Now
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.PageInRole
        SET IsAdd = @IsAdd, IsEdit = @IsEdit, IsRead = @IsRead, IsDelete = @IsDelete,
            IsImport = @IsImport, IsExport = @IsExport, Approve1 = @Approve1, Approve2 = @Approve2,
            LastLogNo = @LastLogNo, Lmodifyby = @ActorSerial, Lmodifydt = @Now
        OUTPUT INSERTED.[RowVersion] INTO @Saved
        WHERE UserId = @UserId AND PathId = @PathId AND DesignationId = @DesignationId;
    END;

    COMMIT TRANSACTION;
    SELECT CONVERT(VARCHAR(16), [RowVersion], 2) AS [RowVersion] FROM @Saved;
END;
GO
