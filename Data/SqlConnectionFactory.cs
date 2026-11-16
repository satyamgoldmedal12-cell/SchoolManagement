using Microsoft.Data.SqlClient;

namespace SchoolManagement.Data;

public sealed class SqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SchoolManagement")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:SchoolManagement for SQL Server.");
    }

    public SqlConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}
