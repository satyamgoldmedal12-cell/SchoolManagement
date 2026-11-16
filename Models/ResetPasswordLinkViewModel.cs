using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class ResetPasswordLinkViewModel
{
    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(4096)]
    public string Code { get; set; } = string.Empty;
}
