using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;
using Xunit;

namespace SchoolManagement.Tests;

public class UserRepositoryTests
{
    [Fact]
    public async Task Dapper_user_updates_share_the_Identity_transaction_and_roll_back_together()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var session = scope.ServiceProvider.GetRequiredService<SqlSession>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await database.UsersAsync()).Single();
        var loginAt = new DateTime(2026, 11, 3, 12, 0, 0, 1, DateTimeKind.Utc);

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await repository.UpdateLastLoginAsync(user.Id, loginAt);
            var list = await repository.ListUsersAsync("admin", 1);
            Assert.Equal(loginAt, Assert.Single(list.Items).LastLoginAt);
            Assert.Equal(loginAt, (await manager.FindByIdAsync(user.Id))!.LastLoginAt);
            await transaction.RollbackAsync();
        }

        Assert.Equal(user.LastLoginAt, ((await database.UsersAsync()).Single()).LastLoginAt);
    }

    [Fact]
    public async Task Repository_keeps_the_session_connection_usable_by_Identity()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<SqlSession>();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByEmailAsync("admin@example.test"))!;
        var connection = session.Connection;

        await connection.OpenAsync();
        Assert.NotNull(await repository.FindUserAsync(user.Id));
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.True(await userManager.IsInRoleAsync(user, AppRoles.Administrator));
        await connection.CloseAsync();

        Assert.NotNull(await repository.FindUserAsync(user.Id));
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.True(await userManager.CheckPasswordAsync(user, SchoolFactory.AdminPassword));
        Assert.NotNull(await userManager.FindByIdAsync(user.Id));
    }
}
