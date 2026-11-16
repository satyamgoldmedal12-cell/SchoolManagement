using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.RepositoryInterfaces;

public interface ISubjectRepositories
{
    Task<IReadOnlyList<Subjectmodel>> ListSubjectsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateSubjectAsync(string name, string actorId, CancellationToken cancellationToken = default);
}
