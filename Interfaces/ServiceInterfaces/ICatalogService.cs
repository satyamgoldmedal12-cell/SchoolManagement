using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface ICatalogService
{
    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DesignationSummary>> GetDesignationsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateDesignationAsync(string name, string roleId, string actorId);
    Task<OperationResult> CreateRoleAsync(string name, string actorId);
}
