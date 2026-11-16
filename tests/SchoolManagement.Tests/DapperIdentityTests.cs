using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Models;
using Xunit;

namespace SchoolManagement.Tests;

public class DapperIdentityTests
{
    [Fact]
    public async Task Claims_logins_roles_and_tokens_survive_a_new_scope()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        string id;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("admin@example.test"))!; id = user.Id;
            Assert.True((await users.AddToRoleAsync(user, "Staff")).Succeeded);
            Assert.True((await users.AddClaimAsync(user, new Claim("campus", "west"))).Succeeded);
            Assert.True((await users.ReplaceClaimAsync(user, new Claim("campus", "west"), new Claim("campus", "east"))).Succeeded);
            Assert.True((await users.AddLoginAsync(user, new UserLoginInfo("TestProvider", "external-id", "Test"))).Succeeded);
            Assert.True((await users.SetAuthenticationTokenAsync(user, "TestProvider", "access", "first")).Succeeded);
            Assert.True((await users.SetAuthenticationTokenAsync(user, "TestProvider", "access", "replacement")).Succeeded);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id))!;
            Assert.Contains("Staff", await users.GetRolesAsync(user));
            Assert.Equal(id, Assert.Single(await users.GetUsersInRoleAsync("Staff")).Id);
            Assert.Contains(await users.GetClaimsAsync(user), c => c.Type == "campus" && c.Value == "east");
            Assert.Equal(id, Assert.Single(await users.GetUsersForClaimAsync(new Claim("campus", "east"))).Id);
            Assert.Equal(id, (await users.FindByLoginAsync("TestProvider", "external-id"))!.Id);
            Assert.Single(await users.GetLoginsAsync(user));
            Assert.Equal("replacement", await users.GetAuthenticationTokenAsync(user, "TestProvider", "access"));
            Assert.True((await users.RemoveAuthenticationTokenAsync(user, "TestProvider", "access")).Succeeded);
            Assert.True((await users.RemoveLoginAsync(user, "TestProvider", "external-id")).Succeeded);
            Assert.True((await users.RemoveClaimAsync(user, new Claim("campus", "east"))).Succeeded);
            Assert.True((await users.RemoveFromRoleAsync(user, "Staff")).Succeeded);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id))!;
            Assert.Null(await users.GetAuthenticationTokenAsync(user, "TestProvider", "access"));
            Assert.Empty(await users.GetLoginsAsync(user)); Assert.Empty(await users.GetClaimsAsync(user));
            Assert.False(await users.IsInRoleAsync(user, "Staff"));
        }
    }

    [Fact]
    public async Task Authenticator_and_recovery_codes_keep_the_existing_Identity_token_format()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        string key; string[] codes;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("admin@example.test"))!;
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            key = (await users.GetAuthenticatorKeyAsync(user))!; Assert.NotEmpty(key);
            codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 3))!.ToArray();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("admin@example.test"))!;
            Assert.Equal(key, await users.GetAuthenticatorKeyAsync(user));
            Assert.Equal(3, await users.CountRecoveryCodesAsync(user));
            Assert.True((await users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
            Assert.False((await users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
            Assert.Equal(2, await users.CountRecoveryCodesAsync(user));
        }
    }

    [Theory]
    [InlineData("role")]
    [InlineData("claim")]
    [InlineData("token")]
    public async Task Stale_user_cannot_overwrite_fields_or_persist_related_changes(string operation)
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var staleScope = factory.Services.CreateScope();
        var users = staleScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stale = (await users.FindByEmailAsync("admin@example.test"))!;
        using (var writer = factory.Services.CreateScope())
        {
            var manager = writer.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var current = (await manager.FindByIdAsync(stale.Id))!; current.FullName = "Concurrent winner";
            Assert.True((await manager.UpdateAsync(current)).Succeeded);
        }
        stale.FullName = "Must not win";
        var result = operation switch
        {
            "role" => await users.AddToRoleAsync(stale, "Staff"),
            "claim" => await users.AddClaimAsync(stale, new Claim("stale", "claim")),
            _ => await users.SetAuthenticationTokenAsync(stale, "Test", "stale", "value")
        };
        Assert.False(result.Succeeded); Assert.Contains(result.Errors, e => e.Code == "ConcurrencyFailure");
        using var verification = factory.Services.CreateScope();
        var freshUsers = verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = (await freshUsers.FindByIdAsync(stale.Id))!;
        Assert.Equal("Concurrent winner", fresh.FullName);
        Assert.False(await freshUsers.IsInRoleAsync(fresh, "Staff"));
        Assert.Empty(await freshUsers.GetClaimsAsync(fresh));
        Assert.Null(await freshUsers.GetAuthenticationTokenAsync(fresh, "Test", "stale"));
    }

    [Fact]
    public async Task Role_claims_check_concurrency_and_persist_removal()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var stale = (await roles.FindByNameAsync("Staff"))!;
        using (var writer = factory.Services.CreateScope())
        {
            var manager = writer.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = (await manager.FindByIdAsync(stale.Id))!;
            Assert.True((await manager.AddClaimAsync(role, new Claim("role-campus", "north"))).Succeeded);
        }
        var failed = await roles.AddClaimAsync(stale, new Claim("stale", "claim"));
        Assert.False(failed.Succeeded); Assert.Contains(failed.Errors, e => e.Code == "ConcurrencyFailure");
        var fresh = (await roles.FindByIdAsync(stale.Id))!;
        Assert.Equal("role-campus", Assert.Single(await roles.GetClaimsAsync(fresh)).Type);
        Assert.True((await roles.RemoveClaimAsync(fresh, new Claim("role-campus", "north"))).Succeeded);
        Assert.Empty(await roles.GetClaimsAsync(fresh));
        var removable = new ApplicationRole { Name = "Disposable" };
        Assert.True((await roles.CreateAsync(removable)).Succeeded);
        Assert.True((await roles.AddClaimAsync(removable, new Claim("cleanup", "yes"))).Succeeded);
        Assert.True((await roles.DeleteAsync(removable)).Succeeded);
        Assert.Null(await roles.FindByIdAsync(removable.Id));
    }

    [Fact]
    public async Task Existing_password_hashes_remain_usable_and_deletion_cascades_identity_records()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "hash@example.test", Email = "hash@example.test", FullName = "Hash compatibility" };
        // Seed a legacy Identity V2 hash independently of the store being tested.
        var legacyHasher = new PasswordHasher<ApplicationUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 }));
        user.PasswordHash = legacyHasher.HashPassword(user, "Legacy-Test-2026!");
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var loaded = (await users.FindByIdAsync(user.Id))!;
        Assert.True(await users.CheckPasswordAsync(loaded, "Legacy-Test-2026!"));
        Assert.False(await users.CheckPasswordAsync(loaded, "Wrong-Test-2026!"));
        Assert.True((await users.AddClaimAsync(loaded, new Claim("cleanup", "yes"))).Succeeded);
        Assert.True((await users.AddLoginAsync(loaded, new UserLoginInfo("Test", "delete-id", "Test"))).Succeeded);
        Assert.True((await users.SetAuthenticationTokenAsync(loaded, "Test", "delete", "token")).Succeeded);
        Assert.True((await users.DeleteAsync(loaded)).Succeeded);
        Assert.Null(await users.FindByIdAsync(loaded.Id)); Assert.Null(await users.FindByLoginAsync("Test", "delete-id"));
    }

    [Fact]
    public async Task Five_failed_password_attempts_lock_the_account_across_requests()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var client = factory.Client();
        for (int i = 0; i < 5; i++)
            await AccountFlowTests.Post(client, "/Account/Login", new() { ["Email"] = "admin@example.test", ["Password"] = "Wrong-Password-2026!" });
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("admin@example.test"))!;
            Assert.True(await users.IsLockedOutAsync(user)); Assert.Equal(0, user.AccessFailedCount);
        }
        var locked = await AccountFlowTests.Post(client, "/Account/Login", new() { ["Email"] = "admin@example.test", ["Password"] = SchoolFactory.AdminPassword });
        Assert.Null(locked.Headers.Location);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TestDatabase>().ExecuteAsync("UPDATE dbo.tblUsers SET LockoutEnd=@past", new { past = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
    }
}
