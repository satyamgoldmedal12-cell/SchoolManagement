using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace SchoolManagement.Models;

public sealed class PageRegistrationModel : IValidatableObject
{
    public int PathId { get; set; }
    public string? RowVersion { get; set; }

    [Required, StringLength(100)]
    [Display(Name = "Page name")]
    public string PageName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a menu icon from the list."), StringLength(ModulePathIcons.MaxKeyLength)]
    [Display(Name = "Menu icon")]
    public string PageSystemName { get; set; } = string.Empty;

    [StringLength(400)]
    [Display(Name = "Page route")]
    public string? PathName { get; set; }

    [Display(Name = "Parent page")]
    public int? HierarchyId { get; set; }
    public string? ParentPageName { get; set; }
    public string? FullPath { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? CreatedDate { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Label ID")]
    public int LabelId { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Sequence ID")]
    public int SequenceId { get; set; }

    [Display(Name = "Leaf page")]
    public bool IsLeafNode { get; set; } = true;

    [Display(Name = "Add")]
    public bool IsAdd { get; set; }
    [Display(Name = "Edit")]
    public bool IsEdit { get; set; }
    [Display(Name = "Read")]
    public bool IsRead { get; set; }
    [Display(Name = "Delete")]
    public bool IsDelete { get; set; }
    [Display(Name = "Import")]
    public bool IsImport { get; set; }
    [Display(Name = "Export")]
    public bool IsExport { get; set; }
    [Display(Name = "Approve 1")]
    public bool Approve1 { get; set; }
    [Display(Name = "Approve 2")]
    public bool Approve2 { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsLeafNode && (PathName is null ||
            !Regex.IsMatch(PathName, @"\A/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+/?\z")))
        {
            yield return new ValidationResult(
                "Enter a page route such as /Classes or /Subject/Index.", [nameof(PathName)]);
        }
    }
}
