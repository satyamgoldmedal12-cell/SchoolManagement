using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SchoolManagement.Data;

public sealed class DatabaseSetup
{
    private readonly SqlConnectionFactory _connections;
    private readonly IWebHostEnvironment _environment;
    public DatabaseSetup(SqlConnectionFactory connections, IWebHostEnvironment environment) { _connections = connections; _environment = environment; }

    public string ReadSchema() => File.ReadAllText(Path.Combine(_environment.ContentRootPath, "Database", "SchoolManagement.sql"));

    public async Task<bool> InitializeAsync()
    {
        await using var connection = _connections.CreateConnection();
        var name = connection.Database;
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_]+$")) throw new InvalidOperationException("Use a database name containing letters, numbers and underscores.");
        var masterSettings = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(masterSettings.ConnectionString);
        if (!await master.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END AS bit)", new { name }))
            await master.ExecuteAsync($"CREATE DATABASE [{name}]");
        if (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables") != 0) return false;
        var script = ReadSchema().Replace("[SchoolManagement]", $"[{name}]").Replace("N'SchoolManagement'", $"N'{name}'");
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await connection.ExecuteAsync(batch);
        return true;
    }
}
