using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface ISchoolRepository
{
    Task<IReadOnlyList<DesignationSummary>> ListDesignationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupItem>> GetDesignationOptionsAsync(CancellationToken cancellationToken = default);
    Task<DesignationAccess?> FindDesignationAsync(int id);
    Task<OperationResult> CreateDesignationAsync(string name, string roleId, string actorId);
}
