

IF COL_LENGTH(N'dbo.tblSubjects', N'createby') IS NULL
    ALTER TABLE dbo.tblSubjects ADD createby INT NULL;
IF COL_LENGTH(N'dbo.tblSubjects', N'createdt') IS NULL
    ALTER TABLE dbo.tblSubjects ADD createdt DATETIME NULL;
IF COL_LENGTH(N'dbo.tblSubjects', N'lmodifyby') IS NULL
    ALTER TABLE dbo.tblSubjects ADD lmodifyby INT NULL;
IF COL_LENGTH(N'dbo.tblSubjects', N'lmodifydt') IS NULL
    ALTER TABLE dbo.tblSubjects ADD lmodifydt DATETIME NULL;
IF COL_LENGTH(N'dbo.tblSubjects', N'deluid') IS NULL
    ALTER TABLE dbo.tblSubjects ADD deluid INT NULL;
IF COL_LENGTH(N'dbo.tblSubjects', N'deludt') IS NULL
    ALTER TABLE dbo.tblSubjects ADD deludt DATETIME NULL;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Subjects_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT SubjectName
    FROM dbo.tblSubjects
    WHERE deludt IS NULL
    ORDER BY SubjectName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Subjects_Create
    @name NVARCHAR(100),
    @actorId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tblSubjects (SubjectName, createby, createdt)
    SELECT @name, u.slno, GETUTCDATE()
    FROM dbo.tblUsers AS u
    WHERE u.Id = @actorId;

    SELECT @@ROWCOUNT;
END;
GO
