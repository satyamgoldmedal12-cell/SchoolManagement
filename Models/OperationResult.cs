namespace SchoolManagement.Models;

public sealed record OperationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    string? Id = null,
    string? Warning = null)
{
    public static OperationResult Success(string? id = null, string? warning = null)
    {
        return new OperationResult(true, Array.Empty<string>(), id, warning);
    }

    public static OperationResult Failure(params string[] errors)
    {
        return new OperationResult(false, errors);
    }
}
