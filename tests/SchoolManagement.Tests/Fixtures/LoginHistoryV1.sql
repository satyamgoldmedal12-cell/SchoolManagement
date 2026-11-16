-- Add login/logout history to an existing SchoolManagement database.
-- Run in the target database. This script is safe to run again.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.tblUserLoginHistory', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tblUserLoginHistory
        (
            Id INT IDENTITY(1,1) NOT NULL,
            SessionId UNIQUEIDENTIFIER NOT NULL,
            UserId NVARCHAR(450) NOT NULL,
            LoginAtUtc DATETIME2(3) NOT NULL,
            LastSeenAtUtc DATETIME2(3) NOT NULL,
            ExpiresAtUtc DATETIME2(3) NOT NULL,
            LogoutAtUtc DATETIME2(3) NULL,
            EndedAtUtc DATETIME2(3) NULL,
            EndReason NVARCHAR(32) NULL,
            IpAddress NVARCHAR(45) NULL,
            UserAgent NVARCHAR(512) NULL,
            CONSTRAINT PK_tblUserLoginHistory PRIMARY KEY (Id),
            CONSTRAINT FK_tblUserLoginHistory_tblUsers_UserId
                FOREIGN KEY (UserId) REFERENCES dbo.tblUsers(Id) ON DELETE NO ACTION,
            CONSTRAINT CK_tblUserLoginHistory_Times CHECK
            (
                LastSeenAtUtc >= LoginAtUtc AND ExpiresAtUtc >= LoginAtUtc
                AND (EndedAtUtc IS NULL OR EndedAtUtc >= LoginAtUtc)
                AND (LogoutAtUtc IS NULL OR (LogoutAtUtc >= LoginAtUtc AND EndedAtUtc IS NOT NULL))
            )
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblUserLoginHistory')
        AND name = N'IX_tblUserLoginHistory_SessionId')
        CREATE UNIQUE INDEX IX_tblUserLoginHistory_SessionId ON dbo.tblUserLoginHistory(SessionId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblUserLoginHistory')
        AND name = N'IX_tblUserLoginHistory_UserId_LoginAtUtc')
        CREATE INDEX IX_tblUserLoginHistory_UserId_LoginAtUtc ON dbo.tblUserLoginHistory(UserId, LoginAtUtc);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblUserLoginHistory')
        AND name = N'IX_tblUserLoginHistory_ExpiresAtUtc')
        CREATE INDEX IX_tblUserLoginHistory_ExpiresAtUtc ON dbo.tblUserLoginHistory(ExpiresAtUtc)
            WHERE EndedAtUtc IS NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
