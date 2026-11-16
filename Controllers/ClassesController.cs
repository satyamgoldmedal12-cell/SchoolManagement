using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class ClassesController : BaseController
{
    private readonly IClassesService _classesService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ClassesController(IClassesService classesService, UserManager<ApplicationUser> userManager)
    {
        _classesService = classesService;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var classes = await _classesService.GetClassesAsync(cancellationToken);
        return View(classes);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Classes());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Classes model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);

        if (user is null || user.slno <= 0)
        {
            ModelState.AddModelError(string.Empty, "The signed-in user's serial number could not be found.");
            return View(model);
        }

        var result = await _classesService.CreateClasses(model, user.slno, cancellationToken);

        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(model);
        }

        ShowResultMessage(result, "Class created.");
        return RedirectToAction(nameof(Index));
    }
}
