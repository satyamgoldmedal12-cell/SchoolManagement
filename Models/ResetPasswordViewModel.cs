using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class ResetPasswordViewModel
{
    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(4096)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
