using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class UsersController : BaseController
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var users = await _userService.GetUsersAsync(search, page, HttpContext.RequestAborted);
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = await PrepareCreateModelAsync(new CreateUserViewModel());
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await PrepareCreateModelAsync(model));
        }

        var command = new CreateUserCommand(
            model.FullName, model.Email, model.PhoneNumber, model.DesignationId.GetValueOrDefault());
        var result = await _userService.CreateAsync(command, CurrentUserId);

        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(await PrepareCreateModelAsync(model));
        }

        ShowResultMessage(result, "User registered. Login details were emailed; first login requires a new password.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SendPasswordLink(string id)
    {
        var result = await _userService.SendAccessEmailAsync(id, CurrentUserId);
        ShowResultMessage(result, "Login email sent successfully.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(SetUserActiveViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _userService.SetActiveAsync(
            model.Id, model.Active.GetValueOrDefault(), model.RowVersion, CurrentUserId);
        ShowResultMessage(result, "User status updated. Previous sessions and links are invalidated.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<CreateUserViewModel> PrepareCreateModelAsync(CreateUserViewModel model)
    {
        var options = await _userService.GetDesignationOptionsAsync(HttpContext.RequestAborted);
        model.Designations = options.Select(option => new SelectListItem
        {
            Text = option.Name,
            Value = option.Id
        }).ToList();
        return model;
    }
}
