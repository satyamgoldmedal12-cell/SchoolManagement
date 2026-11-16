namespace SchoolManagement.Models;

public sealed record CreateUserCommand(
    string FullName,
    string Email,
    string? PhoneNumber,
    int DesignationId);
