namespace SchoolManagement.Models;

public class Designation
{
    public int DesignationId { get; set; }

    public string DesignationName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // The role assigned to this designation.
    public string RoleId { get; set; } = string.Empty;

    public ApplicationRole Role { get; set; } = null!;

    // Tracks when and by whom this designation was changed.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public string? ModifiedBy { get; set; }

    public string? DeletedBy { get; set; }

    public DateTime? DeletedAt { get; set; }

    // Detects updates made by another session.
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
