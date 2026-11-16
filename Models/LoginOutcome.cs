namespace SchoolManagement.Models;

public sealed record LoginOutcome(
    bool Succeeded,
    bool RequiresPasswordChange = false,
    string? Error = null);
