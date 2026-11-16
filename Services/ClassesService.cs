using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Models;

public class ClassesService: IClassesService
{

    public  IClassesRepository  _classes;

    public ClassesService(IClassesRepository classes)
    {
        _classes=classes;
    }

    public Task<IReadOnlyList<Classes>> GetClassesAsync(CancellationToken cancellationToken = default)
    {
        return _classes.ListClassesAsync(cancellationToken);
    }

    public Task<OperationResult> CreateClasses(Classes classes, int creatorSerial, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(classes.ClassName))
        {
            return Task.FromResult(OperationResult.Failure("Subject Name is required."));
        }

        var name = classes.ClassName.Trim();

        return _classes.CreateClassesAsync(name, creatorSerial, cancellationToken);
    }

}

