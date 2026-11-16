using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;
using SchoolManagement.Services;
using Xunit;

namespace SchoolManagement.Tests;

public class DapperRepositoryTests
{
    [Fact]
    public async Task Failed_role_assignment_rolls_back_the_new_identity_user()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using (var scope = factory.Services.CreateScope())
        {
            var sql = scope.ServiceProvider.GetRequiredService<SqlSession>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await using var transaction = await sql.BeginTransactionAsync();
            var user = new ApplicationUser { UserName = "rollback@example.test", Email = "rollback@example.test", FullName = "Rollback User", DesignationId = factory.StaffDesignationId };
            Assert.True((await users.CreateAsync(user, "Rollback-Password-2026!")).Succeeded);
            await Assert.ThrowsAsync<InvalidOperationException>(() => users.AddToRoleAsync(user, "Missing role"));
        }
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TestDatabase>();
        Assert.DoesNotContain(await database.UsersAsync(), user => user.Email == "rollback@example.test");
        Assert.Single(await database.UserRolesAsync());
    }

    [Fact]
    public async Task Administrator_setup_and_password_validation_share_one_transaction()
    {
        await using var factory = new SchoolFactory();
        await InitializeEmpty(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var failed = await accounts.CreateAdministratorAsync("first-admin@example.test", "weak");
            Assert.False(failed.Succeeded);
        }
        using (var verification = factory.Services.CreateScope())
        {
            var database = verification.ServiceProvider.GetRequiredService<TestDatabase>();
            Assert.Empty(await database.RolesAsync());
            Assert.Empty(await database.DesignationsAsync());
            Assert.Empty(await database.UsersAsync());
        }
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministrationBootstrapper>()
                .RunAsync("first-admin@example.test", createAdministrator: true);
        }
        var email = Assert.Single(factory.Email.TemporaryCredentials);
        using var client = factory.Client();
        var login = await AccountFlowTests.Post(client, "/Account/Login", new()
        {
            ["Email"] = email.Email, ["Password"] = email.Password
        });
        Assert.Equal("/Account/ChangeTemporaryPassword", login.Headers.Location?.OriginalString);

        using var finalScope = factory.Services.CreateScope();
        var finalDatabase = finalScope.ServiceProvider.GetRequiredService<TestDatabase>();
        var admin = (await finalDatabase.UsersAsync()).Single();
        Assert.True(admin.MustChangePassword);
        Assert.Equal(2, (await finalDatabase.RolesAsync()).Count);
        Assert.Single(await finalDatabase.DesignationsAsync());
        Assert.Single(await finalDatabase.UserRolesAsync());
        var second = await finalScope.ServiceProvider.GetRequiredService<IAccountRepository>()
            .CreateAdministratorAsync("second-admin@example.test", "Second-Admin-2026!");
        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, error => error.Contains("already exists"));
        Assert.Single(await finalDatabase.UsersAsync());
    }

    [Theory]
    [InlineData("inactive-designation")]
    [InlineData("deleted-designation")]
    [InlineData("deleted-role")]
    public async Task Designation_and_role_restrictions_revoke_existing_logins(string change)
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
            var designation = (await database.DesignationsAsync()).Single(item => item.DesignationName == "Administrator");
            if (change == "inactive-designation") await database.ExecuteAsync("UPDATE dbo.tblDesignations SET IsActive=0 WHERE DesignationId=@id", new { id = designation.DesignationId });
            if (change == "deleted-designation") await database.ExecuteAsync("UPDATE dbo.tblDesignations SET deludt=@now WHERE DesignationId=@id", new { now = DateTime.UtcNow, id = designation.DesignationId });
            if (change == "deleted-role") await database.ExecuteAsync("UPDATE dbo.tblRoles SET deludt=@now WHERE Id=@id", new { now = DateTime.UtcNow, id = designation.RoleId });
        }
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
        using var verification = factory.Services.CreateScope();
        var history = (await verification.ServiceProvider.GetRequiredService<TestDatabase>().HistoryAsync()).Single();
        Assert.Equal(LoginHistoryConstants.Revoked, history.EndReason);
    }

    [Fact]
    public async Task History_touch_preserves_millisecond_precision_and_cannot_reopen_a_session()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        var user = (await database.UsersAsync()).Single();
        var repository = scope.ServiceProvider.GetRequiredService<ILoginHistoryRepository>();
        var loginAt = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var session = new UserLoginHistory
        {
            SessionId = Guid.NewGuid(), UserId = user.Id,
            LoginAtUtc = loginAt, LastSeenAtUtc = loginAt, ExpiresAtUtc = loginAt.AddMinutes(30)
        };
        await repository.CreateAsync(session);
        Assert.True(session.Id > 0);
        var seenAt = loginAt.AddMilliseconds(1);
        var expiry = loginAt.AddMinutes(31).AddMilliseconds(1);
        Assert.True(await repository.TouchAsync(session.SessionId, user.Id, seenAt, expiry));
        Assert.True(await repository.TouchAsync(session.SessionId, user.Id, loginAt, loginAt.AddMinutes(30)));
        Assert.False(await repository.TouchAsync(session.SessionId, "another-user", seenAt, expiry));
        var updated = (await database.HistoryAsync()).Single();
        Assert.Equal(seenAt, updated.LastSeenAtUtc);
        Assert.Equal(expiry, updated.ExpiresAtUtc);
        await repository.EndSessionAsync(session.SessionId, user.Id, seenAt, LoginHistoryConstants.Logout);
        Assert.False(await repository.TouchAsync(session.SessionId, user.Id, seenAt, expiry));
        await repository.EndSessionAsync(session.SessionId, user.Id, seenAt.AddSeconds(1), LoginHistoryConstants.Revoked);
        var closed = (await database.HistoryAsync()).Single();
        Assert.Equal(LoginHistoryConstants.Logout, closed.EndReason);
        Assert.Equal(seenAt, closed.LogoutAtUtc);
    }

    [Fact]
    public async Task History_pagination_and_literal_search_do_not_accept_sql_or_wildcards()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        var user = (await database.UsersAsync()).Single();
        await database.ExecuteAsync("UPDATE dbo.tblUsers SET FullName=@name WHERE Id=@id", new { name = "Admin %_ Match", id = user.Id });
        var repository = scope.ServiceProvider.GetRequiredService<ILoginHistoryRepository>();
        var now = DateTime.UtcNow;
        for (int i = 0; i < 53; i++)
        {
            await repository.CreateAsync(new UserLoginHistory
            {
                SessionId = Guid.NewGuid(), UserId = user.Id,
                LoginAtUtc = now, LastSeenAtUtc = now, ExpiresAtUtc = now.AddMinutes(30)
            });
        }
        var first = await repository.ListAsync(" %_ ", 0);
        var second = await repository.ListAsync("%_", 2);
        Assert.Equal(50, first.Items.Count);
        Assert.True(first.HasNext);
        Assert.Equal(3, second.Items.Count);
        Assert.False(second.HasNext);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.True(first.Items[0].Id > first.Items[1].Id);
        Assert.Empty((await repository.ListAsync("'; DROP TABLE tblUsers;--", 1)).Items);
        Assert.Single(await database.UsersAsync());
    }

    private static async Task InitializeEmpty(SchoolFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        Assert.StartsWith("SchoolManagementTests_", database.Name);
        await using var connection = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().CreateConnection();
        Assert.Equal(database.Name, connection.Database);
        await database.EnsureCreatedAsync();
        await StoredProcedureSetup.ApplyAsync(database);
    }
}
