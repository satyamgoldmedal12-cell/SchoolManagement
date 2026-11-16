using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class Subjectmodel
{
    public const int MaxNameLength = 100;

    [Required(ErrorMessage = "Subject Name is required")]
    [StringLength(MaxNameLength, ErrorMessage = "Subject Name must be at most 100 characters.")]
    [Display(Name = "Subject Name")]
    public string SubjectName { get; set; } = string.Empty;
}
