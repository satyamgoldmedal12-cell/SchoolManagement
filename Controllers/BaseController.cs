using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

public abstract class BaseController : Controller
{
    protected string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)?? throw new InvalidOperationException("The signed-in user ID is missing.");
    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The signed-in user ID is missing.");


    protected void AddModelErrors(OperationResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    protected void ShowResultMessage(OperationResult result, string successMessage)
    {
        if (!result.Succeeded)
        {
            TempData["Warning"] = string.Join(" ", result.Errors);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            TempData["Warning"] = result.Warning;
            return;
        }

        TempData["Success"] = successMessage;
    }
}
