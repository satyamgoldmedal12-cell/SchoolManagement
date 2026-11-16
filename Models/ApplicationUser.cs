using Microsoft.AspNetCore.Identity;

namespace SchoolManagement.Models;

public class ApplicationUser : IdentityUser
{
    public int slno { get; set; }

    public string FullName { get; set; } = string.Empty;

    public int? DesignationId { get; set; }

    public Designation? Designation { get; set; }

    public bool IsActive { get; set; } = true;

    // Tracks who created, updated or deleted the account.
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public string? DeletedBy { get; set; }

    public DateTime? DeletedAt { get; set; }

    // Tracks login and first-password setup.
    public DateTime? LastLoginAt { get; set; }

    public DateTime? InvitationSentAt { get; set; }

    public DateTime? PasswordSetAt { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTime? TemporaryPasswordExpiresAt { get; set; }

    // Detects updates made by another session.
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
