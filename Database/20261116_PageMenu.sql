-- Run in the database used by the SchoolManagement connection string.
CREATE OR ALTER PROCEDURE dbo.usp_PageInRole_UserMenu
    @IdentityUserId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT m.PathId, ISNULL(m.HierarchyId, 0) AS HierarchyId, m.PageName,
           m.PageSystemName, m.PathName, m.IsLeafNode,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsRead = 1
                             THEN COALESCE(userRight.IsRead, designationRight.IsRead, 0) ELSE 0 END) AS CanRead,
           CONVERT(BIT, CASE WHEN m.IsLeafNode = 1 AND p.IsAdd = 1
                             THEN COALESCE(userRight.IsAdd, designationRight.IsAdd, 0) ELSE 0 END) AS CanAdd
    FROM dbo.tblUsers AS u
    CROSS JOIN dbo.ModulePath AS m
    INNER JOIN dbo.PageSetting AS p ON p.PageSettingId = m.PageSettingId
    LEFT JOIN dbo.tblDesignations AS d ON d.DesignationId = u.DesignationId
        AND d.IsActive = 1 AND d.deludt IS NULL
    LEFT JOIN dbo.PageInRole AS userRight ON userRight.PathId = m.PathId
        AND userRight.[Type] = 1 AND userRight.UserId = u.slno AND userRight.DesignationId = 0 AND u.slno > 0
    LEFT JOIN dbo.PageInRole AS designationRight ON designationRight.PathId = m.PathId
        AND designationRight.[Type] = 2 AND designationRight.UserId = 0 AND designationRight.DesignationId = d.DesignationId
    WHERE u.Id = @IdentityUserId AND u.IsActive = 1 AND u.deludt IS NULL
      AND m.ApplicationId = 1 AND m.IsActive = 1 AND m.deludt IS NULL AND p.DeletedDt IS NULL
    ORDER BY m.SequenceId, m.PageName, m.PathId;
END;
GO
