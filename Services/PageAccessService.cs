using System.Security.Claims;
using Microsoft.Data.SqlClient;
using SchoolManagement.Models;
using SchoolManagement.Repositories;

namespace SchoolManagement.Services;

public sealed class PageAccessService(PageInRoleRepository repository, ILogger<PageAccessService> logger)
{
    private Task<IReadOnlyList<PageAccessEntry>>? _pages;

    public Task<IReadOnlyList<PageAccessEntry>> GetPagesAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identityUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return identityUserId is null
            ? Task.FromResult<IReadOnlyList<PageAccessEntry>>([])
            : _pages ??= LoadPagesAsync(identityUserId, cancellationToken);
    }

    public async Task<bool> CanAccessAsync(
        ClaimsPrincipal user, string controller, bool requireAdd, CancellationToken cancellationToken)
    {
        var pages = await GetPagesAsync(user, cancellationToken);
        return HasAccess(pages, controller, requireAdd);
    }

    public static bool HasAccess(IEnumerable<PageAccessEntry> pages, string controller, bool requireAdd)
    {
        // The JSON user list shares the Users module's Read right.
        var module = controller.Equals("UsersApi", StringComparison.OrdinalIgnoreCase) ? "Users" : controller;
        return pages.Any(page => page.CanRead && (!requireAdd || page.CanAdd)
            && IndexPath(page.PathName).Equals("/" + module, StringComparison.OrdinalIgnoreCase));
    }

    public static string IndexPath(string? path)
    {
        var normalized = path?.TrimEnd('/') ?? string.Empty;
        if (normalized.Length > 6 && normalized.EndsWith("/Index", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6];
        }

        // Older Module Path entries use /Account for the Users list.
        return normalized.Equals("/Account", StringComparison.OrdinalIgnoreCase) ? "/Users" : normalized;
    }

    private async Task<IReadOnlyList<PageAccessEntry>> LoadPagesAsync(string identityUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await repository.ListForUserAsync(identityUserId, cancellationToken);
        }
        catch (SqlException exception)
        {
            logger.LogWarning(exception, "Could not load Module Path rights for the current request.");
            return [];
        }
    }
}
