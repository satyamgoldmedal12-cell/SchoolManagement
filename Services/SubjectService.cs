using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class SubjectService : ISubjectService
{
    private readonly ISubjectRepositories _subjects;

    public SubjectService(ISubjectRepositories subjects)
    {
        _subjects = subjects;
    }

    public Task<IReadOnlyList<Subjectmodel>> GetSubjectsAsync(CancellationToken cancellationToken = default)
    {
        return _subjects.ListSubjectsAsync(cancellationToken);
    }

    public Task<OperationResult> CreateSubject(Subjectmodel subject, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject.SubjectName))
        {
            return Task.FromResult(OperationResult.Failure("Subject Name is required."));
        }

        var name = subject.SubjectName.Trim();
        if (name.Length > Subjectmodel.MaxNameLength)
        {
            return Task.FromResult(OperationResult.Failure("Subject Name must be at most 100 characters."));
        }

        return _subjects.CreateSubjectAsync(name, actorId, cancellationToken);
    }
}
