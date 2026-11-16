using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class ChangeTemporaryPasswordViewModel
{
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

    public LoginDeviceInput Device { get; set; } = new LoginDeviceInput();
}
