using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class PageInRoleController(PageInRoleService pageRights) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index(
        PageRightsType? type, int? targetId, CancellationToken cancellationToken)
    {
        if (type.HasValue && !Enum.IsDefined(type.Value))
        {
            ModelState.AddModelError(string.Empty, "Choose a valid type.");
            type = null;
        }

        var model = await pageRights.SearchAsync(type, targetId, cancellationToken);
        if (targetId.HasValue && !model.Searched)
        {
            ModelState.AddModelError(string.Empty, "Choose a valid user or designation.");
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Options(PageRightsType type, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(type))
        {
            return BadRequest();
        }

        return Json(await pageRights.ListTargetsAsync(type, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] PageRightsUpdate? update, CancellationToken cancellationToken)
    {
        if (update is null || !ModelState.IsValid)
        {
            return BadRequest(new { message = "The selected rights or row version are invalid." });
        }

        Guid? sessionId = Guid.TryParse(User.FindFirst(LoginHistoryConstants.SessionClaim)?.Value, out var session)
            ? session
            : null;
        try
        {
            var rowVersion = await pageRights.SaveAsync(update, CurrentUserId, sessionId, cancellationToken);
            return Json(new { rowVersion });
        }
        catch (SqlException exception) when (exception.Number is >= 51040 and <= 51043)
        {
            return exception.Number == 51043
                ? Conflict(new { message = exception.Message })
                : BadRequest(new { message = exception.Message });
        }
    }
}
