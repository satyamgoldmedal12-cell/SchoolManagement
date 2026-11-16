using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class RolesController : BaseController
{
    private readonly ICatalogService _catalogService;

    public RolesController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var roles = await _catalogService.GetRolesAsync(HttpContext.RequestAborted);
        return View(roles);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateRoleViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _catalogService.CreateRoleAsync(model.Name, CurrentUserId);
        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(model);
        }

        ShowResultMessage(result, "Role created.");
        return RedirectToAction(nameof(Index));
    }
}
