using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SchoolManagement.Models;

public class CreateDesignationViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Designation name")]
    public string DesignationName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Role")]
    public string RoleId { get; set; } = string.Empty;

    public List<SelectListItem> Roles { get; set; } = new List<SelectListItem>();
}
