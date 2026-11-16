using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class SchoolRepository : ISchoolRepository
{
    private readonly SqlConnectionFactory _connections;

    public SchoolRepository(SqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<DesignationSummary>> ListDesignationsAsync(
        CancellationToken cancellationToken = default)
    {
        const string procedure = "dbo.usp_Designations_List";

        var command = new CommandDefinition(procedure, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = CreateConnection();
        var designations = await connection.QueryAsync<DesignationSummary>(command);
        return designations.ToList();
    }

    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(
        CancellationToken cancellationToken = default)
    {
        const string procedure = "dbo.usp_Roles_List";

        var command = new CommandDefinition(procedure, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = CreateConnection();
        var roles = await connection.QueryAsync<RoleSummary>(command);
        return roles.ToList();
    }

    public async Task<IReadOnlyList<LookupItem>> GetDesignationOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string procedure = "dbo.usp_Designations_Options";

        var command = new CommandDefinition(procedure, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);

        await using var connection = CreateConnection();
        var options = await connection.QueryAsync<LookupItem>(command);
        return options.ToList();
    }

    public async Task<DesignationAccess?> FindDesignationAsync(int id)
    {
        const string procedure = "dbo.usp_Designations_Find";

        var parameters = new { id };

        await using var connection = CreateConnection();
        var designation = await connection.QuerySingleOrDefaultAsync<DesignationAccess>(procedure, parameters, commandType: CommandType.StoredProcedure);
        return designation;
    }

    public async Task<OperationResult> CreateDesignationAsync(string name, string roleId, string actorId)
    {
        const string procedure = "dbo.usp_Designations_Create";

        var parameters = new { name, roleId, actorId };

        await using var connection = CreateConnection();
        try
        {
            int insertedRows = await connection.ExecuteScalarAsync<int>(procedure, parameters, commandType: CommandType.StoredProcedure);
            if (insertedRows != 1)
            {
                return OperationResult.Failure("Choose a valid role.");
            }

            return OperationResult.Success();
        }
        catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
        {
            return OperationResult.Failure("This designation already exists.");
        }
    }

    private SqlConnection CreateConnection()
    {
        return _connections.CreateConnection();
    }
}
