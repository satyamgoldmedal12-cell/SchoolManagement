using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface IPageRepository
{
    Task<IReadOnlyList<PageRegistrationModel>> ListAsync(CancellationToken cancellationToken);
    Task<OperationResult> CreateAsync(PageRegistrationModel page, int creatorSerial, CancellationToken cancellationToken);
    Task<OperationResult> DeleteAsync(int pathId, string? rowVersion, int creatorSerial, CancellationToken cancellationToken);
}
