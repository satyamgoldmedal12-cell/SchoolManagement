using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface IUserService
{
    Task<PagedResult<UserSummary>> GetUsersAsync(string? search, int page, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupItem>> GetDesignationOptionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(CreateUserCommand command, string actorId);
    Task<OperationResult> SendAccessEmailAsync(string id, string actorId);
    Task<OperationResult> SetActiveAsync(string id, bool active, byte[] rowVersion, string actorId);
}
