using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class LoginHistoryController : Controller
{
    private readonly ILoginHistoryService _loginHistory;

    public LoginHistoryController(ILoginHistoryService loginHistory)
    {
        _loginHistory = loginHistory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var history = await _loginHistory.GetHistoryAsync(search, page, HttpContext.RequestAborted);
        return View(history);
    }
}
