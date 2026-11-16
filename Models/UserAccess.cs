namespace SchoolManagement.Models;

public sealed record UserAccess(
    string Id,
    string FullName,
    string Email,
    bool IsActive,
    bool MustChangePassword);
