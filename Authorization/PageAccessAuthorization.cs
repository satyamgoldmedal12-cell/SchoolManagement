using Microsoft.AspNetCore.Authorization;
using SchoolManagement.Services;

namespace SchoolManagement.Authorization;

public sealed record PageAccessRequirement(bool RequireAdd) : IAuthorizationRequirement
{
    public const string ReadPolicy = "ModulePageRead";
    public const string AddPolicy = "ModulePageAdd";
}

public sealed class PageAccessHandler(PageAccessService pageAccess) : AuthorizationHandler<PageAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PageAccessRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && httpContext.Request.RouteValues["controller"] is string controller
            && await pageAccess.CanAccessAsync(context.User, controller, requirement.RequireAdd, httpContext.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
