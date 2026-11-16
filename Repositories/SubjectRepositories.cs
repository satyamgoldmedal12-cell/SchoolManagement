using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class SubjectRepositories : ISubjectRepositories
{
    private readonly SqlConnectionFactory _connections;

    public SubjectRepositories(SqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<Subjectmodel>> ListSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "dbo.usp_Subjects_List",
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await using var connection = _connections.CreateConnection();
        var subjects = await connection.QueryAsync<Subjectmodel>(command);
        return subjects.ToList();
    }

    public async Task<OperationResult> CreateSubjectAsync(string name, string actorId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("name", name);
        parameters.Add("actorId", actorId);

        var command = new CommandDefinition(
            "dbo.usp_Subjects_Create",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await using var connection = _connections.CreateConnection();
        try
        {
            int insertedRows = await connection.ExecuteScalarAsync<int>(command);
            if (insertedRows == 0)
            {
                return OperationResult.Failure("The signed-in user could not be found.");
            }

            return OperationResult.Success();
        }
        catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
        {
            return OperationResult.Failure("This subject already exists.");
        }
    }
}
