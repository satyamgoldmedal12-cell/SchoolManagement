USE [SchoolManagement];
GO

-- Older installations used VARCHAR(20) for the icon key.
IF COL_LENGTH(N'dbo.ModulePath', N'PageSystemName') BETWEEN 1 AND 63
    ALTER TABLE dbo.ModulePath ALTER COLUMN PageSystemName VARCHAR(64) NOT NULL;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pages_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT m.PathId, CONVERT(VARCHAR(16), CONVERT(BINARY(8), m.RowVersion), 2) AS RowVersion,
           m.PageName, m.PageSystemName, m.PathName, m.HierarchyId,
           parent.PageName AS ParentPageName, m.FullPath, m.LabelId, m.SequenceId, m.IsLeafNode,
           COALESCE(NULLIF(LTRIM(RTRIM(creator.FullName)), N''), NULLIF(creator.UserName, N''),
                    CONVERT(NVARCHAR(20), m.createBy)) AS CreatedByName,
           m.createDate AS CreatedDate,
           p.IsAdd, p.IsEdit, p.IsRead, p.IsDelete, p.IsImport, p.IsExport,
           ISNULL(p.[Approve 1], 0) AS Approve1,
           ISNULL(p.[approve 2], 0) AS Approve2
    FROM dbo.ModulePath AS m
    INNER JOIN dbo.PageSetting AS p ON p.PageSettingId = m.PageSettingId
    LEFT JOIN dbo.ModulePath AS parent ON parent.PathId = m.HierarchyId
    LEFT JOIN dbo.tblUsers AS creator ON creator.slno = m.createBy
    WHERE m.ApplicationId = 1
      AND m.IsActive = 1
      AND m.deludt IS NULL
      AND p.DeletedDt IS NULL
    ORDER BY m.SequenceId, m.PageName, m.PathId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pages_Create
    @pageName VARCHAR(100),
    @pageSystemName VARCHAR(64),
    @pathName VARCHAR(400),
    @creatorSerial INT,
    @isAdd BIT,
    @isEdit BIT,
    @isRead BIT,
    @isDelete BIT,
    @isImport BIT,
    @isExport BIT,
    @approve1 BIT,
    @approve2 BIT,
    @pathId INT = 0,
    @rowVersion VARCHAR(16) = NULL,
    @hierarchyId INT = NULL,
    @isLeafNode BIT = 1,
    @labelId INT = 0,
    @sequenceId INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @applicationId TINYINT = 1;
    DECLARE @now DATETIME2(7) = SYSUTCDATETIME();
    DECLARE @pageSettingId INT;
    DECLARE @parentPath VARCHAR(MAX);
    DECLARE @fullPath VARCHAR(MAX);
    DECLARE @descendants TABLE (PathId INT PRIMARY KEY);
    DECLARE @result INT = 1;

    IF @isLeafNode = 0
    BEGIN
        SELECT @pathName = '#', @isAdd = 0, @isEdit = 0, @isRead = 0, @isDelete = 0,
               @isImport = 0, @isExport = 0, @approve1 = 0, @approve2 = 0;
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Serialize writes to the hierarchy and duplicate checks.
        SELECT @pageSettingId = PageSettingId
        FROM dbo.ModulePath WITH (TABLOCKX, HOLDLOCK)
        WHERE PathId = @pathId AND ApplicationId = @applicationId
          AND IsActive = 1 AND deludt IS NULL
          AND RowVersion = TRY_CONVERT(BINARY(8), @rowVersion, 2);

        IF @pathId <> 0 AND @pageSettingId IS NULL
            SET @result = -1;

        IF @result = 1 AND @pathId <> 0
        BEGIN
            ;WITH descendants AS
            (
                SELECT PathId FROM dbo.ModulePath
                WHERE HierarchyId = @pathId AND ApplicationId = @applicationId AND deludt IS NULL
                UNION ALL
                SELECT m.PathId FROM dbo.ModulePath AS m
                JOIN descendants AS d ON m.HierarchyId = d.PathId
                WHERE m.ApplicationId = @applicationId AND m.deludt IS NULL
            )
            INSERT @descendants (PathId)
            SELECT PathId FROM descendants
            OPTION (MAXRECURSION 32767);

            IF @isLeafNode = 1 AND EXISTS (SELECT 1 FROM @descendants)
                SET @result = -3;
        END;

        IF @result = 1 AND @hierarchyId IS NOT NULL
        BEGIN
            SELECT @parentPath = COALESCE(NULLIF(FullPath, ''), PageName)
            FROM dbo.ModulePath
            WHERE PathId = @hierarchyId AND ApplicationId = @applicationId
              AND IsActive = 1 AND deludt IS NULL AND IsLeafNode = 0;

            IF @parentPath IS NULL OR @hierarchyId = @pathId
                OR EXISTS (SELECT 1 FROM @descendants WHERE PathId = @hierarchyId)
                SET @result = -2;
        END;

        IF @result = 1 AND EXISTS
        (
            SELECT 1 FROM dbo.ModulePath
            WHERE ApplicationId = @applicationId AND deludt IS NULL AND PathId <> @pathId
              AND @isLeafNode = 1 AND IsLeafNode = 1 AND LOWER(PathName) = LOWER(@pathName)
        )
            SET @result = 0;

        IF @result = 1 AND @pathId <> 0 AND EXISTS
        (
            SELECT 1 FROM dbo.ModulePath
            WHERE PageSettingId = @pageSettingId AND PathId <> @pathId AND deludt IS NULL
        )
            SET @result = -4;

        IF @result <> 1
        BEGIN
            COMMIT TRANSACTION;
            SELECT @result;
            RETURN;
        END;

        SET @fullPath = CONCAT(@parentPath + ' >> ', @pageName);

        IF @pathId = 0
        BEGIN
            INSERT dbo.PageSetting
            (
                IsAdd, IsEdit, IsRead, IsDelete, IsImport, IsExport,
                [Approve 1], [approve 2], UniqueKey, CreatedBy, CreatedDt
            )
            VALUES
            (
                @isAdd, @isEdit, @isRead, @isDelete, @isImport, @isExport,
                @approve1, @approve2, NEWID(), @creatorSerial, @now
            );

            SET @pageSettingId = CONVERT(INT, SCOPE_IDENTITY());

            INSERT dbo.ModulePath
            (
                PageSettingId, ApplicationId, HierarchyId, PageName, PageSystemName,
                PathName, PathLowerName, UniqueKey, LastLogNo, IsLeafNode,
                LabelId, SequenceId, FullPath, IsActive, createDate, createBy
            )
            VALUES
            (
                @pageSettingId, @applicationId, @hierarchyId, @pageName, @pageSystemName,
                @pathName, LOWER(@pathName), NEWID(), 0, @isLeafNode,
                @labelId, @sequenceId, @fullPath, 1, @now, @creatorSerial
            );

            SET @pathId = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.PageSetting
            SET IsAdd = @isAdd, IsEdit = @isEdit, IsRead = @isRead, IsDelete = @isDelete,
                IsImport = @isImport, IsExport = @isExport, [Approve 1] = @approve1,
                [approve 2] = @approve2, ModifyBy = @creatorSerial, ModifyDt = @now
            WHERE PageSettingId = @pageSettingId AND DeletedDt IS NULL;

            IF @@ROWCOUNT <> 1
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT -1;
                RETURN;
            END;

            UPDATE dbo.ModulePath
            SET HierarchyId = @hierarchyId, PageName = @pageName, PageSystemName = @pageSystemName,
                PathName = @pathName, PathLowerName = LOWER(@pathName), IsLeafNode = @isLeafNode,
                LabelId = @labelId, SequenceId = @sequenceId, FullPath = @fullPath,
                modifyDate = @now, modifyBy = @creatorSerial
            WHERE PathId = @pathId;

            ;WITH paths AS
            (
                SELECT m.PathId, CAST(CONCAT(@fullPath, ' >> ', m.PageName) AS VARCHAR(MAX)) AS FullPath
                FROM dbo.ModulePath AS m JOIN @descendants AS d ON d.PathId = m.PathId
                WHERE m.HierarchyId = @pathId
                UNION ALL
                SELECT m.PathId, CAST(CONCAT(p.FullPath, ' >> ', m.PageName) AS VARCHAR(MAX))
                FROM dbo.ModulePath AS m
                JOIN paths AS p ON m.HierarchyId = p.PathId
                JOIN @descendants AS d ON d.PathId = m.PathId
            )
            UPDATE m SET FullPath = p.FullPath, modifyDate = @now, modifyBy = @creatorSerial
            FROM dbo.ModulePath AS m JOIN paths AS p ON p.PathId = m.PathId
            WHERE m.FullPath IS NULL OR CONVERT(VARBINARY(MAX), m.FullPath) <> CONVERT(VARBINARY(MAX), p.FullPath)
            OPTION (MAXRECURSION 32767);
        END;

        COMMIT TRANSACTION;
        SELECT @pathId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pages_Delete
    @pathId INT,
    @rowVersion VARCHAR(16),
    @creatorSerial INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @pageSettingId INT;
    DECLARE @now DATETIME2(7) = SYSUTCDATETIME();

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @pageSettingId = PageSettingId
        FROM dbo.ModulePath WITH (TABLOCKX, HOLDLOCK)
        WHERE PathId = @pathId AND ApplicationId = 1
          AND IsActive = 1 AND deludt IS NULL
          AND RowVersion = TRY_CONVERT(BINARY(8), @rowVersion, 2);

        IF @pageSettingId IS NULL
        BEGIN
            COMMIT TRANSACTION;
            SELECT -1;
            RETURN;
        END;

        IF EXISTS (SELECT 1 FROM dbo.ModulePath WHERE HierarchyId = @pathId AND deludt IS NULL)
        BEGIN
            COMMIT TRANSACTION;
            SELECT -3;
            RETURN;
        END;

        UPDATE dbo.ModulePath
        SET IsActive = 0, deluid = @creatorSerial, deludt = @now
        WHERE PathId = @pathId;

        IF NOT EXISTS (SELECT 1 FROM dbo.ModulePath WHERE PageSettingId = @pageSettingId AND deludt IS NULL)
        BEGIN
            UPDATE dbo.PageSetting
            SET Deletedby = @creatorSerial, DeletedDt = @now
            WHERE PageSettingId = @pageSettingId AND DeletedDt IS NULL;
        END;

        COMMIT TRANSACTION;
        SELECT @pathId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
