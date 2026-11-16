namespace SchoolManagement.Models;

public sealed class PageAccessEntry
{
    public int PathId { get; set; }
    public int HierarchyId { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string? PageSystemName { get; set; }
    public string? PathName { get; set; }
    public bool IsLeafNode { get; set; }
    public bool CanRead { get; set; }
    public bool CanAdd { get; set; }
}

public sealed record PageMenuItem(
    string Name, string? Url, string IconClass, bool IsCurrent, IReadOnlyList<PageMenuItem> Children,
    bool ExpandByDefault = false)
{
    public bool IsOpen => ExpandByDefault || IsCurrent || Children.Any(item => item.IsOpen);
}

public sealed record PageMenuViewModel(
    IReadOnlyList<PageMenuItem> Pages, IReadOnlyList<PageMenuItem> AdministratorLinks);
