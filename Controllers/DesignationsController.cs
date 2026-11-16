using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class DesignationsController : BaseController
{
    private readonly ICatalogService _catalogService;

    public DesignationsController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var designations = await _catalogService.GetDesignationsAsync(HttpContext.RequestAborted);
        return View(designations);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = await PrepareCreateModelAsync(new CreateDesignationViewModel());
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateDesignationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await PrepareCreateModelAsync(model));
        }

        var result = await _catalogService.CreateDesignationAsync(model.DesignationName, model.RoleId, CurrentUserId);
        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(await PrepareCreateModelAsync(model));
        }

        ShowResultMessage(result, "Designation created.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<CreateDesignationViewModel> PrepareCreateModelAsync(CreateDesignationViewModel model)
    {
        var roles = await _catalogService.GetRolesAsync(HttpContext.RequestAborted);
        model.Roles = roles.Select(role => new SelectListItem
        {
            Text = role.Name,
            Value = role.Id
        }).ToList();
        return model;
    }
}
