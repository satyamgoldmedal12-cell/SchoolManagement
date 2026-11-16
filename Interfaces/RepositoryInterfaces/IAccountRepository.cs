using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface IAccountRepository
{
    Task<OperationResult> CreateRoleAsync(string name, string actorId);
    Task<OperationResult> CreateAdministratorAsync(string email, string password);
}
