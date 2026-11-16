using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly ISchoolRepository _schoolRepository;
    private readonly IAccountRepository _accountRepository;

    public CatalogService(ISchoolRepository schoolRepository, IAccountRepository accountRepository)
    {
        _schoolRepository = schoolRepository;
        _accountRepository = accountRepository;
    }

    public Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return _schoolRepository.ListRolesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DesignationSummary>> GetDesignationsAsync(CancellationToken cancellationToken = default)
    {
        return _schoolRepository.ListDesignationsAsync(cancellationToken);
    }

    public Task<OperationResult> CreateDesignationAsync(string name, string roleId, string actorId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || string.IsNullOrWhiteSpace(roleId))
        {
            var result = OperationResult.Failure("Provide a designation name and valid role.");
            return Task.FromResult(result);
        }

        return _schoolRepository.CreateDesignationAsync(name.Trim(), roleId, actorId);
    }

    public Task<OperationResult> CreateRoleAsync(string name, string actorId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 256)
        {
            var result = OperationResult.Failure("Provide a role name with at most 256 characters.");
            return Task.FromResult(result);
        }

        return _accountRepository.CreateRoleAsync(name.Trim(), actorId);
    }
}
