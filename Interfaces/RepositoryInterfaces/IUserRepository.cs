using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface IUserRepository
{
    Task<PagedResult<UserSummary>> ListUsersAsync(string? search, int page, CancellationToken cancellationToken = default);
    Task<UserAccess?> FindUserAsync(string id);
    Task MarkEmailSentAsync(string id, string? actorId);
    Task<bool> SetUserActiveAsync(string id, bool active, byte[] rowVersion, string actorId);
    Task UpdateLastLoginAsync(string id, DateTime loginAtUtc);
    Task<bool> IsActiveDesignationAsync(int id, string roleId);
}
