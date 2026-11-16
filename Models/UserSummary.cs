namespace SchoolManagement.Models;

public sealed class UserSummary
{
    public string Id { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }

    public string? DesignationName { get; init; }

    public string? RoleName { get; init; }

    public bool IsActive { get; init; }

    public bool MustChangePassword { get; init; }

    public DateTime? LastLoginAt { get; init; }

    public DateTime? InvitationSentAt { get; init; }

    public DateTime? PasswordSetAt { get; init; }

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}
