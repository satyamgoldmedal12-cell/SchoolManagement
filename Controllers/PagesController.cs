using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class PagesController : BaseController
{
    private readonly IPageService _pages;
    private readonly UserManager<ApplicationUser> _userManager;

    public PagesController(IPageService pages, UserManager<ApplicationUser> userManager)
    {
        _pages = pages;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        ViewData["Search"] = search?.Trim();
        return View(await _pages.ListAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await LoadParentsAsync(0, cancellationToken);
        return View(new PageRegistrationModel());
    }

    [HttpPost]
    public Task<IActionResult> Create(PageRegistrationModel page, CancellationToken cancellationToken)
    {
        page.PathId = 0;
        return SaveAsync(page, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var pages = await _pages.ListAsync(cancellationToken);
        var entry = pages.FirstOrDefault(item => item.PathId == id);
        if (entry is null)
        {
            return NotFound();
        }

        SetParents(pages, id);
        return View("Create", entry);
    }

    [HttpPost]
    public Task<IActionResult> Edit(int id, PageRegistrationModel page, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        page.PathId = id;
        return SaveAsync(page, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var pages = await _pages.ListAsync(cancellationToken);
        var entry = pages.FirstOrDefault(item => item.PathId == id);
        if (entry is null)
        {
            return NotFound();
        }

        return View(entry);
    }

    [HttpPost, ActionName(nameof(Delete))]
    public async Task<IActionResult> ConfirmDelete(int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || user.slno <= 0)
        {
            return Forbid();
        }

        var result = await _pages.DeleteAsync(id, rowVersion, user.slno, cancellationToken);
        ShowResultMessage(result, "Module path deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SaveAsync(PageRegistrationModel page, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null || user.slno <= 0)
            {
                return Forbid();
            }

            var result = await _pages.CreateAsync(page, user.slno, cancellationToken);
            if (result.Succeeded)
            {
                ShowResultMessage(result, page.PathId == 0 ? "Module path added." : "Module path updated.");
                return RedirectToAction(nameof(Index));
            }

            AddModelErrors(result);
        }

        await LoadParentsAsync(page.PathId, cancellationToken);
        return View("Create", page);
    }

    private async Task LoadParentsAsync(int pathId, CancellationToken cancellationToken)
    {
        SetParents(await _pages.ListAsync(cancellationToken), pathId);
    }

    private void SetParents(IReadOnlyList<PageRegistrationModel> pages, int pathId)
    {
        ViewData["ParentPages"] = pages.Where(entry => !entry.IsLeafNode && entry.PathId != pathId).ToList();
        var iconKey = pages.FirstOrDefault(entry => entry.PathId == pathId)?.PageSystemName;

        if (!string.IsNullOrEmpty(iconKey) && !ModulePathIcons.Contains(iconKey))
        {
            ViewData["LegacyIconKey"] = iconKey;
        }
    }
}
