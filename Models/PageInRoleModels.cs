using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public enum PageRightsType
{
    User = 1,
    Designation = 2
}

public sealed record PageRightsTarget(int Id, string Name);

public sealed record PageInRoleViewModel(
    PageRightsType? Type,
    int? TargetId,
    IReadOnlyList<PageRightsTarget> Targets,
    IReadOnlyList<PageRightsRow> Pages,
    bool Searched);

public class PageRights
{
    public bool IsAdd { get; set; }
    public bool IsEdit { get; set; }
    public bool IsRead { get; set; }
    public bool IsDelete { get; set; }
    public bool IsImport { get; set; }
    public bool IsExport { get; set; }
    public bool Approve1 { get; set; }
    public bool Approve2 { get; set; }
}

public sealed class PageRightsRow : PageRights
{
    public int PathId { get; set; }
    public int HierarchyId { get; set; }
    public string PageName { get; set; } = string.Empty;
    public bool IsLeafNode { get; set; }
    public string? RowVersion { get; set; }
    public bool CanAdd { get; set; }
    public bool CanEdit { get; set; }
    public bool CanRead { get; set; }
    public bool CanDelete { get; set; }
    public bool CanImport { get; set; }
    public bool CanExport { get; set; }
    public bool CanApprove1 { get; set; }
    public bool CanApprove2 { get; set; }
}

public sealed class PageRightsUpdate : PageRights
{
    [EnumDataType(typeof(PageRightsType))]
    public PageRightsType Type { get; set; }

    [Range(1, int.MaxValue)]
    public int TargetId { get; set; }

    [Range(1, int.MaxValue)]
    public int PathId { get; set; }

    [RegularExpression("[0-9A-Fa-f]{16}")]
    public string? RowVersion { get; set; }
}
