using SchoolManagement.Models;

namespace SchoolManagement.Interfaces.ServiceInterfaces;

public interface ISubjectService
{
    Task<IReadOnlyList<Subjectmodel>> GetSubjectsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateSubject(Subjectmodel subject, string actorId, CancellationToken cancellationToken = default);
}
