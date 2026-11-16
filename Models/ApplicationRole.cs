using Microsoft.AspNetCore.Identity;

namespace SchoolManagement.Models;

public class ApplicationRole : IdentityRole
{
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public string? DeletedBy { get; set; }

    public DateTime? DeletedAt { get; set; }
}
