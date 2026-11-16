namespace SchoolManagement.Models;

public sealed record DesignationAccess(
    int Id,
    string Name,
    string RoleId,
    string RoleName,
    bool IsActive);
