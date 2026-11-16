
using SchoolManagement.Models;

public interface IClassesService
{
    Task<IReadOnlyList<Classes>> GetClassesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateClasses(Classes classes, int creatorSerial, CancellationToken cancellationToken = default);
}
