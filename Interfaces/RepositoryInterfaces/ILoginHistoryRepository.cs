using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface ILoginHistoryRepository
{
    Task CreateAsync(UserLoginHistory session);
    Task<bool> TouchAsync(Guid sessionId, string userId, DateTime seenAtUtc, DateTime expiresAtUtc);
    Task EndSessionAsync(Guid sessionId, string userId, DateTime endedAtUtc, string reason);
    Task EndUserSessionsAsync(string userId, DateTime endedAtUtc, string reason);
    Task CloseExpiredSessionsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<PagedResult<LoginHistorySummary>> ListAsync(string? search, int page, CancellationToken cancellationToken = default);
}
