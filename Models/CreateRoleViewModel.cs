using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class CreateRoleViewModel
{
    [Required]
    [StringLength(256)]
    [Display(Name = "Role name")]
    public string Name { get; set; } = string.Empty;
}
