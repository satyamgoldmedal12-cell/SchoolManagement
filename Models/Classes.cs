
using System.ComponentModel.DataAnnotations;

public class Classes
{
    [Required(ErrorMessage = "Class name is required.")]
    [Display(Name = "Class name")]
    public string ClassName { get; set; } = string.Empty;



}

