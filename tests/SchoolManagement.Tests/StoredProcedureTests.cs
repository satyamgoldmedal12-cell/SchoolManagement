using System.Net;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using Xunit;

namespace SchoolManagement.Tests;

public class StoredProcedureTests
{
    [Fact]
    public async Task Procedure_deployment_is_repeatable_and_keeps_users_and_login_history()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        var user = (await database.UsersAsync()).Single();
        var history = (await database.HistoryAsync()).Single();
        await using var sql = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().CreateConnection();
        var names = (await sql.QueryAsync<string>("SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID('dbo');"))
            .Order().ToArray();
        Assert.Equal(45, names.Length);

        await StoredProcedureSetup.ApplyAsync(database);
        await StoredProcedureSetup.ApplyAsync(database);

        Assert.Equal(names, (await sql.QueryAsync<string>("SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID('dbo');")).Order().ToArray());
        var retainedUser = (await database.UsersAsync()).Single();
        Assert.Equal(user.Id, retainedUser.Id);
        Assert.Equal(user.PasswordHash, retainedUser.PasswordHash);
        var retainedHistory = (await database.HistoryAsync()).Single();
        Assert.Equal(history.SessionId, retainedHistory.SessionId);
        Assert.Equal(history.LoginAtUtc, retainedHistory.LoginAtUtc);
        Assert.Null(retainedHistory.EndedAtUtc);
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        Assert.Single((await repository.ListUsersAsync(null, 1)).Items);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/LoginHistory")).StatusCode);
    }
}
