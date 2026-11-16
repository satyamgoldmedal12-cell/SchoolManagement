using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class ForgotPasswordViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;
}
