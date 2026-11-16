using System.Data;
using Dapper;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class PageRepository : IPageRepository
{
    private readonly SqlConnectionFactory _connections;

    public PageRepository(SqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<PageRegistrationModel>> ListAsync(CancellationToken cancellationToken)
    {
        var command = new CommandDefinition("dbo.usp_Pages_List",
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = _connections.CreateConnection();
        var pages = await connection.QueryAsync<PageRegistrationModel>(command);
        return pages.AsList();
    }

    public async Task<OperationResult> CreateAsync(
        PageRegistrationModel page, int creatorSerial, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pageName", page.PageName, DbType.AnsiString, size: 100);
        parameters.Add("pageSystemName", page.PageSystemName, DbType.AnsiString, size: ModulePathIcons.MaxKeyLength);
        parameters.Add("pathName", page.PathName, DbType.AnsiString, size: 400);
        parameters.Add("creatorSerial", creatorSerial);
        parameters.Add("isAdd", page.IsAdd);
        parameters.Add("isEdit", page.IsEdit);
        parameters.Add("isRead", page.IsRead);
        parameters.Add("isDelete", page.IsDelete);
        parameters.Add("isImport", page.IsImport);
        parameters.Add("isExport", page.IsExport);
        parameters.Add("approve1", page.Approve1);
        parameters.Add("approve2", page.Approve2);
        parameters.Add("pathId", page.PathId);
        parameters.Add("rowVersion", page.RowVersion, DbType.AnsiString, size: 16);
        parameters.Add("hierarchyId", page.HierarchyId);
        parameters.Add("isLeafNode", page.IsLeafNode);
        parameters.Add("labelId", page.LabelId);
        parameters.Add("sequenceId", page.SequenceId);

        return await SaveResultAsync("dbo.usp_Pages_Create", parameters, cancellationToken);
    }

    public Task<OperationResult> DeleteAsync(int pathId, string? rowVersion, int creatorSerial, CancellationToken cancellationToken)
    {
        return SaveResultAsync("dbo.usp_Pages_Delete", new { pathId, rowVersion, creatorSerial }, cancellationToken);
    }

    private async Task<OperationResult> SaveResultAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = _connections.CreateConnection();
        var result = await connection.QuerySingleAsync<int>(command);
        return result switch
        {
            > 0 => OperationResult.Success(),
            0 => OperationResult.Failure("A page with this route already exists."),
            -2 => OperationResult.Failure("Choose a valid parent folder outside this page's branch."),
            -3 => OperationResult.Failure("Move this folder's child entries first."),
            -4 => OperationResult.Failure("These rights are shared with another page. Update the shared setting before changing this entry."),
            _ => OperationResult.Failure("This entry changed or was removed. Reload the list and try again.")
        };
    }
}
