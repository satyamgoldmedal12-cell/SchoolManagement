using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Data;
using Xunit;

namespace SchoolManagement.Tests;

public class SchemaUpgradeTests
{
    [Fact]
    public async Task Supplied_schema_upgrades_without_data_loss_and_can_be_run_twice()
    {
        var name = "SchoolManagementTests_" + Guid.NewGuid().ToString("N");
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("SCHOOLMANAGEMENT_TEST_SQL_SERVER") ?? "(localdb)\\SchoolManagementTests15",
            InitialCatalog = "master", IntegratedSecurity = true, TrustServerCertificate = true
        };
        await using var master = new SqlConnection(settings.ToString());
        await master.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE [{name}]", master)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.InitialCatalog = name;
            await using var connection = new SqlConnection(settings.ToString());
            await connection.OpenAsync();
            const string legacy = """
                CREATE TABLE tblUsers (
                    Id NVARCHAR(450) NOT NULL PRIMARY KEY, UserName NVARCHAR(256), NormalizedUserName NVARCHAR(256),
                    Email NVARCHAR(256), NormalizedEmail NVARCHAR(256), PasswordHash NVARCHAR(MAX),
                    SecurityStamp NVARCHAR(MAX), ConcurrencyStamp NVARCHAR(MAX), PhoneNumber NVARCHAR(MAX),
                    TwoFactorEnabled BIT NOT NULL DEFAULT(0), LockoutEnd DATETIMEOFFSET,
                    LockoutEnabled BIT NOT NULL DEFAULT(1), AccessFailedCount INT NOT NULL DEFAULT(0),
                    designationid INT, IsActive BIT NOT NULL DEFAULT(1), Createdby INT,
                    CreatedAt DATETIME2(3) NOT NULL DEFAULT(SYSUTCDATETIME()), Lmodifyby INT, Lmodifydt DATETIME,
                    deluid INT, deludt DATETIME, LastLoginAt DATETIME2(3)
                );
                CREATE TABLE tblRoles (
                    Id NVARCHAR(450) NOT NULL PRIMARY KEY, Name NVARCHAR(256), NormalizedName NVARCHAR(256),
                    ConcurrencyStamp NVARCHAR(MAX), Createdby INT,
                    CreatedAt DATETIME2(3) NOT NULL DEFAULT(SYSUTCDATETIME()), Lmodifyby INT,
                    Lmodifydt DATETIME, deluid INT, deludt DATETIME
                );
                CREATE TABLE tblDesignations (
                    DesignationId INT IDENTITY(1,1) NOT NULL PRIMARY KEY, DesignationName NVARCHAR(100) NOT NULL UNIQUE,
                    IsActive BIT NOT NULL DEFAULT(1), Roleid INT, CreatedAt DATETIME2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
                    CreatedBy NVARCHAR(450), ModifiedAt DATETIME2(3), ModifiedBy NVARCHAR(450),
                    deluid INT, deludt DATETIME, RowVersion ROWVERSION NOT NULL
                );
                INSERT INTO tblRoles(Id,Name,Createdby) VALUES(N'1',N'Teacher',12);
                INSERT INTO tblDesignations(DesignationName,Roleid) VALUES(N'Teacher',1);
                INSERT INTO tblUsers(Id,UserName,Email,designationid,Createdby,PasswordHash)
                VALUES(N'legacy-user',N'legacy@school.test',N'legacy@school.test',1,12,N'existing-hash-unchanged');
                """;
            await using (var command = new SqlCommand(legacy, connection)) await command.ExecuteNonQueryAsync();
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SchoolManagement.sql"));
            script = script.Replace("[SchoolManagement]", $"[{name}]").Replace("N'SchoolManagement'", $"N'{name}'");
            await using (var invalidReference = new SqlCommand("UPDATE tblDesignations SET Roleid=999", connection))
                await invalidReference.ExecuteNonQueryAsync();
            var error = await Assert.ThrowsAsync<SqlException>(async () =>
            {
                foreach (var batch in Regex.Split(script, @"(?m)^GO\s*$").Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    await using var command = new SqlCommand(batch, connection);
                    await command.ExecuteNonQueryAsync();
                }
            });
            Assert.Contains("Map every designation", error.Message);
            await using (var rolledBack = new SqlCommand("SELECT COL_LENGTH('dbo.tblUsers', 'FullName')", connection))
                Assert.Equal(DBNull.Value, await rolledBack.ExecuteScalarAsync());
            await using (var repair = new SqlCommand("UPDATE tblDesignations SET Roleid=1", connection))
                await repair.ExecuteNonQueryAsync();
            foreach (var pass in new[] { 1, 2 })
            {
                foreach (var batch in Regex.Split(script, @"(?m)^GO\s*$").Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    await using var command = new SqlCommand(batch, connection);
                    await command.ExecuteNonQueryAsync();
                }
            }
            var db = new TestDatabase(settings.ToString());
            await StoredProcedureSetup.ApplyAsync(db);
            var user = (await db.UsersAsync()).Single();
            Assert.Equal("legacy-user", user.Id);
            Assert.Equal("existing-hash-unchanged", user.PasswordHash);
            Assert.Equal("12", user.CreatedBy);
            Assert.Equal("LEGACY@SCHOOL.TEST", user.NormalizedEmail);
            var designation = (await db.DesignationsAsync()).Single(x => x.DesignationId == user.DesignationId);
            Assert.Equal("Teacher", (await db.RolesAsync()).Single(x => x.Id == designation.RoleId).Name);
            Assert.NotEmpty(user.RowVersion);
            Assert.Single(await db.UserRolesAsync());
            Assert.Equal(9, await CountTables(connection));
            Assert.Empty(await db.HistoryAsync());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            Assert.StartsWith("SchoolManagementTests_", name);
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];", master);
            await drop.ExecuteNonQueryAsync();
        }
    }
    [Fact]
    public async Task Login_history_upgrade_can_be_applied_twice_to_existing_users()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        Assert.StartsWith("SchoolManagementTests_", database.Name);
        Assert.Empty(await database.HistoryAsync());
        var originalUser = (await database.UsersAsync()).Single();

        // Only the empty table in this isolated test database is removed.
        await database.ExecuteAsync("DROP TABLE dbo.tblUserLoginHistory;");
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "20261031_LoginHistory.sql"));
        await database.ExecuteAsync(script);
        await database.ExecuteAsync(script);
        await StoredProcedureSetup.ApplyAsync(database);

        var retainedUser = (await database.UsersAsync()).Single();
        Assert.Equal(originalUser.Id, retainedUser.Id);
        Assert.Equal(originalUser.PasswordHash, retainedUser.PasswordHash);

        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        await AccountFlowTests.Post(client, "/Account/Logout", new(), "/");
        var history = (await database.HistoryAsync()).Single();
        Assert.Equal("Logout", history.EndReason);
        Assert.NotNull(history.LogoutAtUtc);
    }

    private static async Task<int> CountTables(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'tbl%'", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
