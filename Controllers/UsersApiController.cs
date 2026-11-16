using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = AppRoles.Administrator)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class UsersApiController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersApiController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserSummary>>> Get(
        string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var users = await _userService.GetUsersAsync(search, page, cancellationToken);
        return Ok(users);
    }
}
