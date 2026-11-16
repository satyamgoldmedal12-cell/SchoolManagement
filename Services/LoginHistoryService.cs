using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class LoginHistoryService : ILoginHistoryService
{
    private readonly ILoginHistoryRepository _repository;
    private readonly TimeProvider _clock;
    private readonly ILoginMetadataService _metadata;
    private readonly ILoginAreaService _area;
    private readonly IGeoLocationService _geoLocation;

    public LoginHistoryService(ILoginHistoryRepository repository, TimeProvider clock,
        ILoginMetadataService metadata, ILoginAreaService area, IGeoLocationService geoLocation)
    {
        _repository = repository;
        _clock = clock;
        _metadata = metadata;
        _area = area;
        _geoLocation = geoLocation;
    }

    public async Task<Guid> StartSessionAsync(ApplicationUser user, HttpContext context,
        AuthenticationProperties properties, LoginDeviceInput? device = null)
    {
        if (properties.IssuedUtc is null || properties.ExpiresUtc is null)
        {
            throw new InvalidOperationException("A login session must have an issue time and expiry time.");
        }

        await EndCurrentSessionAsync(context, LoginHistoryConstants.Replaced);
        await _repository.CloseExpiredSessionsAsync(_clock.GetUtcNow().UtcDateTime);

        var session = new UserLoginHistory
        {
            SessionId = Guid.NewGuid(),
            UserId = user.Id,
            LoginAtUtc = properties.IssuedUtc.Value.UtcDateTime,
            LastSeenAtUtc = properties.IssuedUtc.Value.UtcDateTime,
            ExpiresAtUtc = properties.ExpiresUtc.Value.UtcDateTime
        };

        _metadata.ApplyTo(session, context, device);
        _area.ApplyTo(session);
        await _geoLocation.ApplyAsync(session, context.RequestAborted);
        await _repository.CreateAsync(session);
        return session.SessionId;
    }

    public async Task EndCurrentSessionAsync(HttpContext context, string reason)
    {
        var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authentication.Succeeded)
        {
            return;
        }

        var sessionId = authentication.Principal?.FindFirstValue(LoginHistoryConstants.SessionClaim);
        var userId = authentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        await EndSessionAsync(sessionId, userId, reason);
    }

    public async Task EndSessionAsync(string? sessionId, string? userId, string reason)
    {
        if (userId is null || !Guid.TryParse(sessionId, out var parsedSessionId))
        {
            return;
        }

        await _repository.EndSessionAsync(parsedSessionId, userId, _clock.GetUtcNow().UtcDateTime, reason);
    }

    public Task EndUserSessionsAsync(string userId, string reason)
    {
        return _repository.EndUserSessionsAsync(userId, _clock.GetUtcNow().UtcDateTime, reason);
    }

    public async Task ValidateSessionAsync(CookieValidatePrincipalContext context, string? sessionId)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !Guid.TryParse(sessionId, out var parsedSessionId))
        {
            await RejectSessionAsync(context);
            return;
        }

        var now = _clock.GetUtcNow();
        var expiresAt = context.Properties.ExpiresUtc;
        if (expiresAt is null)
        {
            await RejectSessionAsync(context);
            return;
        }

        // Match the cookie handler's renewal calculation, including Identity stamp refreshes.
        if (context.ShouldRenew && context.Properties.IssuedUtc.HasValue)
        {
            var cookieLifetime = expiresAt.Value - context.Properties.IssuedUtc.Value;
            expiresAt = now.Add(cookieLifetime);
        }

        bool active = await _repository.TouchAsync(parsedSessionId, userId, now.UtcDateTime, expiresAt.Value.UtcDateTime);
        if (!active)
        {
            await _repository.CloseExpiredSessionsAsync(now.UtcDateTime);
            await RejectSessionAsync(context);
            return;
        }

        // Identity rebuilds the principal during security-stamp validation.
        // Copy this session's marker to the refreshed identity so it stays the same session.
        var identity = context.Principal!.Identity as ClaimsIdentity;
        if (identity is null)
        {
            await RejectSessionAsync(context);
            return;
        }

        foreach (var claim in identity.FindAll(LoginHistoryConstants.SessionClaim).ToList())
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(LoginHistoryConstants.SessionClaim, parsedSessionId.ToString()));
    }

    public async Task<PagedResult<LoginHistorySummary>> GetHistoryAsync(
        string? search, int page, CancellationToken cancellationToken = default)
    {
        await _repository.CloseExpiredSessionsAsync(_clock.GetUtcNow().UtcDateTime, cancellationToken);
        return await _repository.ListAsync(search, page, cancellationToken);
    }

    private static async Task RejectSessionAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

}
