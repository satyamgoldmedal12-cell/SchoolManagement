
using Dapper;
using Microsoft.Data.SqlClient;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Models;
using System.Data;

public class ClassesRepository: IClassesRepository
{
    private readonly SqlConnectionFactory _connections;

    public ClassesRepository(SqlConnectionFactory connections )
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<Classes>> ListClassesAsync(CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "dbo.usp_classes_List",
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await using var connection = _connections.CreateConnection();
        var subjects = await connection.QueryAsync<Classes>(command);

        return subjects.ToList();
    }

    public async Task<OperationResult> CreateClassesAsync(string name, int creatorSerial, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("classesName", name);
        parameters.Add("uid", creatorSerial, DbType.Int32);

        var command = new CommandDefinition(
            "dbo.usp_Classess_create",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        try
        {
            int result = await connection.ExecuteScalarAsync<int>(command);
            if (result == 2)
            {
                return OperationResult.Failure("This class already exists.");
            }

            if (result != 1)
            {
                return OperationResult.Failure("The class could not be saved.");
            }

            return OperationResult.Success();
        }
        catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
        {
            return OperationResult.Failure("This class already exists.");
        }
    }

}

