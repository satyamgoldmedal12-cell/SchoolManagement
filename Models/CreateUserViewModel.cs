using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SchoolManagement.Models;

public class CreateUserViewModel
{
    [Required]
    [StringLength(150)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [StringLength(30)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Required]
    [Display(Name = "Designation")]
    public int? DesignationId { get; set; }

    public List<SelectListItem> Designations { get; set; } = new List<SelectListItem>();
}
