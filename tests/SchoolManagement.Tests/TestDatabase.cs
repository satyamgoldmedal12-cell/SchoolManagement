using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Tests;

// Test fixtures use SQL directly, always against a disposable, uniquely named database.
public sealed class TestDatabase
{
    private readonly string _connectionString;
    public string Name { get; }
    public TestDatabase(SqlConnectionFactory connections) : this(connections.CreateConnection().ConnectionString) { }
    public TestDatabase(string connectionString)
    {
        _connectionString = connectionString;
        Name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!Regex.IsMatch(Name, @"^SchoolManagementTests_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Tests require an isolated SchoolManagementTests database.");
    }
    public async Task<List<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        return (await connection.QueryAsync<T>(sql, parameters)).ToList();
    }
    public Task<List<ApplicationUser>> UsersAsync() => QueryAsync<ApplicationUser>("""
        SELECT *, Createdby AS CreatedBy, Lmodifyby AS ModifiedBy, Lmodifydt AS ModifiedAt,
            deluid AS DeletedBy, deludt AS DeletedAt FROM dbo.tblUsers;
        """);
    public Task<List<ApplicationRole>> RolesAsync() => QueryAsync<ApplicationRole>("""
        SELECT *, Createdby AS CreatedBy, Lmodifyby AS ModifiedBy, Lmodifydt AS ModifiedAt,
            deluid AS DeletedBy, deludt AS DeletedAt FROM dbo.tblRoles;
        """);
    public Task<List<Designation>> DesignationsAsync() => QueryAsync<Designation>("SELECT *, deluid AS DeletedBy, deludt AS DeletedAt FROM dbo.tblDesignations;");
    public Task<List<IdentityUserRole<string>>> UserRolesAsync() => QueryAsync<IdentityUserRole<string>>("SELECT * FROM dbo.tblUserRoles;");
    public Task<List<UserLoginHistory>> HistoryAsync() => QueryAsync<UserLoginHistory>("SELECT * FROM dbo.tblUserLoginHistory ORDER BY Id;");
    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, parameters);
    }
    public async Task InsertDesignationAsync(Designation designation)
    {
        await using var connection = new SqlConnection(_connectionString);
        designation.DesignationId = await connection.QuerySingleAsync<int>("""
            INSERT dbo.tblDesignations (DesignationName,Roleid,IsActive,CreatedAt)
            OUTPUT inserted.DesignationId VALUES (@DesignationName,@RoleId,@IsActive,@CreatedAt);
            """, designation);
    }
    public async Task ExecuteScriptAsync(string script)
    {
        script = script.Replace("[SchoolManagement]", $"[{Name}]").Replace("N'SchoolManagement'", $"N'{Name}'");
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await connection.ExecuteAsync(batch);
    }
    public async Task EnsureCreatedAsync()
    {
        var settings = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(settings.ConnectionString);
        if (!await master.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN DB_ID(@Name) IS NULL THEN 0 ELSE 1 END AS bit)", new { Name }))
            await master.ExecuteAsync($"CREATE DATABASE [{Name}]");
        var tables = await QueryAsync<int>("SELECT COUNT(*) FROM sys.tables;");
        if (tables.Single() == 0) await ExecuteScriptAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SchoolManagement.sql")));
    }
    public async Task EnsureDeletedAsync()
    {
        SqlConnection.ClearAllPools();
        var settings = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(settings.ConnectionString);
        await master.ExecuteAsync($"IF DB_ID(@Name) IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END", new { Name });
    }
}
