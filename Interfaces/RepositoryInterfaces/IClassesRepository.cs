
using SchoolManagement.Models;
namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface IClassesRepository
{
    Task<IReadOnlyList<Classes>> ListClassesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateClassesAsync(string name, int creatorSerial, CancellationToken cancellationToken = default);
}

