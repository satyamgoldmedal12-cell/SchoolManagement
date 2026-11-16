namespace SchoolManagement.Models;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    bool HasNext,
    string? Search);
