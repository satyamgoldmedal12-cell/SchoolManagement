namespace SchoolManagement.Models;

public sealed record DesignationSummary(
    int DesignationId,
    string DesignationName,
    string RoleName,
    bool IsActive);
