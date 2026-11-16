using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface ILoginHistoryService
{
    Task<Guid> StartSessionAsync(ApplicationUser user, HttpContext context, AuthenticationProperties properties, LoginDeviceInput? device = null);
    Task EndCurrentSessionAsync(HttpContext context, string reason);
    Task EndSessionAsync(string? sessionId, string? userId, string reason);
    Task EndUserSessionsAsync(string userId, string reason);
    Task ValidateSessionAsync(CookieValidatePrincipalContext context, string? sessionId);
    Task<PagedResult<LoginHistorySummary>> GetHistoryAsync(string? search, int page, CancellationToken cancellationToken = default);
}
