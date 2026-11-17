using System.Data;
using Dapper;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class PageInRoleRepository(SqlConnectionFactory connections)
{
    public async Task<IReadOnlyList<PageRightsTarget>> ListTargetsAsync(
        PageRightsType type, CancellationToken cancellationToken)
    {
        const string procedure = "dbo.usp_PageInRole_Targets";
        var command = new CommandDefinition(procedure, new { Type = (int)type },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = connections.CreateConnection();
        return (await connection.QueryAsync<PageRightsTarget>(command)).AsList();
    }

    public async Task<IReadOnlyList<PageRightsRow>> ListPagesAsync(
        PageRightsType type, int targetId, CancellationToken cancellationToken)
    {
        const string procedure = "dbo.usp_PageInRole_List";
        var command = new CommandDefinition(procedure, new { Type = (int)type, TargetId = targetId },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = connections.CreateConnection();
        return (await connection.QueryAsync<PageRightsRow>(command)).AsList();
    }

    public async Task<IReadOnlyList<PageAccessEntry>> ListForUserAsync(
        string identityUserId, CancellationToken cancellationToken)
    {
        const string procedure = "dbo.usp_PageInRole_UserMenu";
        var command = new CommandDefinition(procedure, new { IdentityUserId = identityUserId },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = connections.CreateConnection();
        return (await connection.QueryAsync<PageAccessEntry>(command)).AsList();
    }

    public async Task<string> SaveAsync(
        PageRightsUpdate update, string actorId, Guid? sessionId, CancellationToken cancellationToken)
    {
        const string procedure = "dbo.usp_PageInRole_Save";
        var parameters = new
        {
            Type = (int)update.Type,
            update.TargetId,
            update.PathId,
            update.RowVersion,
            update.IsAdd,
            update.IsEdit,
            update.IsRead,
            update.IsDelete,
            update.IsImport,
            update.IsExport,
            update.Approve1,
            update.Approve2,
            ActorId = actorId,
            SessionId = sessionId
        };
        var command = new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = connections.CreateConnection();
        return await connection.QuerySingleAsync<string>(command);
    }
}
