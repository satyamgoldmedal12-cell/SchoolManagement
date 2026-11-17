using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using SchoolManagement.Authorization;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.ViewComponents;

public sealed class PageMenuViewComponent(
    PageAccessService pageAccess, IActionDescriptorCollectionProvider actions) : ViewComponent
{
    private static readonly (string Controller, string Name, string Icon)[] AdministrationTools =
    [
        ("Pages", "Module Path", "fi fi-rr-folder"),
        ("PageInRole", "Page in Role", "fi fi-rr-key")
    ];

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = HttpContext.User;
        var isAdministrator = user.IsInRole(AppRoles.Administrator);
        var pages = await pageAccess.GetPagesAsync(user, HttpContext.RequestAborted);
        var ids = pages.Select(page => page.PathId).ToHashSet();
        var children = pages.ToLookup(page => ids.Contains(page.HierarchyId) ? page.HierarchyId : 0);
        var currentPath = PageAccessService.IndexPath(HttpContext.Request.Path.Value);
        var shownControllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var menu = BuildBranch(0);
        var administratorLinks = isAdministrator
            ? AdministrationTools
                .Where(item => !shownControllers.Contains(item.Controller))
                .Select(item => new PageMenuItem(item.Name, Url.Action("Index", item.Controller), item.Icon,
                    IsCurrent("/" + item.Controller), []))
                .ToArray()
            : [];

        return View(new PageMenuViewModel(menu, administratorLinks));

        List<PageMenuItem> BuildBranch(int parentId)
        {
            var branch = new List<PageMenuItem>();
            foreach (var page in children[parentId])
            {
                var icon = ModulePathIcons.GetCssClass(page.PageSystemName);
                if (!page.IsLeafNode)
                {
                    var descendants = BuildBranch(page.PathId);
                    if (descendants.Count > 0)
                    {
                        branch.Add(new PageMenuItem(page.PageName, null, icon ?? "fi fi-rr-folder", false, descendants,
                            ExpandByDefault: parentId == 0));
                    }
                    continue;
                }

                if (GetLink(page.PathName) is not { } link)
                {
                    continue;
                }

                var isSetupTool = AdministrationTools.Any(item =>
                    item.Controller.Equals(link.Controller, StringComparison.OrdinalIgnoreCase));
                if ((link.RequiresAdministrator || isSetupTool) && !isAdministrator)
                {
                    continue;
                }

                if (!isSetupTool && (!page.CanRead || (link.RequiresModuleAccess
                    && !PageAccessService.HasAccess(pages, link.Controller, link.RequiresAdd))))
                {
                    continue;
                }

                if (link.IsIndex)
                {
                    shownControllers.Add(link.Controller);
                }

                var currentMenuPath = link.IsIndex ? "/" + link.Controller : page.PathName;
                branch.Add(new PageMenuItem(page.PageName, link.Url, icon ?? "fi fi-rr-document",
                    IsCurrent(currentMenuPath), []));
            }

            return branch;
        }

        bool IsCurrent(string? path) =>
            string.Equals(PageAccessService.IndexPath(path), currentPath, StringComparison.OrdinalIgnoreCase);
    }

    private (string Url, string Controller, bool IsIndex, bool RequiresAdministrator,
        bool RequiresModuleAccess, bool RequiresAdd)? GetLink(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || !Url.IsLocalUrl(path))
        {
            return null;
        }

        var segments = path.Trim('/').Split('/');
        if (segments.Length > 3 || segments.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        var controller = segments[0];
        var action = segments.ElementAtOrDefault(1) ?? "Index";
        var isIndex = segments.Length < 3 && action.Equals("Index", StringComparison.OrdinalIgnoreCase);
        if (isIndex)
        {
            controller = PageAccessService.IndexPath(path)[1..];
        }

        var endpoint = actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().FirstOrDefault(item =>
            item.AttributeRouteInfo is null
            && item.ControllerName.Equals(controller, StringComparison.OrdinalIgnoreCase)
            && item.ActionName.Equals(action, StringComparison.OrdinalIgnoreCase)
            && (item.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                .All(constraint => constraint.HttpMethods.Contains("GET", StringComparer.OrdinalIgnoreCase)) ?? true));

        if (endpoint is null)
        {
            return null;
        }

        var url = Url.Action(action, controller, new { id = segments.ElementAtOrDefault(2) });
        if (url is null)
        {
            return null;
        }

        var access = endpoint.EndpointMetadata.OfType<IAuthorizeData>().ToArray();
        var requiresAdministrator = access.Any(rule => rule.Roles == AppRoles.Administrator);
        var requiresAdd = access.Any(rule => rule.Policy == PageAccessRequirement.AddPolicy);
        var requiresModuleAccess = requiresAdd || access.Any(rule => rule.Policy == PageAccessRequirement.ReadPolicy);
        return (url, controller, isIndex, requiresAdministrator, requiresModuleAccess, requiresAdd);
    }
}
