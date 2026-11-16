using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;
using SchoolManagement.Services;
using Xunit;

namespace SchoolManagement.Tests;

public class LoginHistoryTests
{
    [Fact]
    public async Task Separate_browsers_keep_separate_sessions_and_logout_blocks_cookie_replay()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var first = factory.Client();
        using var second = factory.Client();
        first.DefaultRequestHeaders.UserAgent.ParseAdd("HistoryBrowserA/1.0");
        second.DefaultRequestHeaders.UserAgent.ParseAdd("HistoryBrowserB/1.0");

        var login = await AccountFlowTests.Post(first, "/Account/Login", new()
        {
            ["Email"] = "admin@example.test",
            ["Password"] = SchoolFactory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var originalCookie = login.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("SchoolManagement.Auth=", StringComparison.Ordinal))
            .Split(';')[0];

        var firstSession = Assert.Single(await History(factory));
        Assert.Equal("HistoryBrowserA/1.0", firstSession.UserAgent);
        Assert.Null(firstSession.LogoutAtUtc);

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Home/Privacy")).StatusCode);
        var renewed = Assert.Single(await History(factory));
        Assert.Equal(firstSession.SessionId, renewed.SessionId);
        Assert.True(renewed.LastSeenAtUtc >= firstSession.LastSeenAtUtc);
        Assert.True(renewed.ExpiresAtUtc >= firstSession.ExpiresAtUtc);

        await AccountFlowTests.Login(second, "admin@example.test", SchoolFactory.AdminPassword);
        Assert.Equal(2, (await History(factory)).Count);
        var page = await second.GetStringAsync("/LoginHistory?search=admin%40example.test");
        Assert.Contains("HistoryBrowserA/1.0", page);
        Assert.Contains("HistoryBrowserB/1.0", page);
        Assert.Contains("Login (UTC)", page);

        await AccountFlowTests.Post(first, "/Account/Logout", new(), "/");
        var sessions = await History(factory);
        var closed = Assert.Single(sessions, session => session.UserAgent == "HistoryBrowserA/1.0");
        Assert.Equal(LoginHistoryConstants.Logout, closed.EndReason);
        Assert.NotNull(closed.LogoutAtUtc);
        Assert.Equal(closed.LogoutAtUtc, closed.EndedAtUtc);
        Assert.Null(Assert.Single(sessions, session => session.UserAgent == "HistoryBrowserB/1.0").EndedAtUtc);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/")).StatusCode);

        using var replay = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost")
        });
        replay.DefaultRequestHeaders.Add("Cookie", originalCookie);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.GetAsync("/")).StatusCode);
        Assert.Equal(LoginHistoryConstants.Logout,
            (await History(factory)).Single(session => session.Id == closed.Id).EndReason);
    }

    [Fact]
    public async Task Failed_and_temporary_logins_are_not_completed_sessions_and_history_is_admin_only()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var admin = factory.Client();
        using var staff = factory.Client();
        using var anonymous = factory.Client();

        await AccountFlowTests.Post(anonymous, "/Account/Login", new()
        {
            ["Email"] = "admin@example.test", ["Password"] = "Wrong-Password-2026!"
        });
        Assert.Empty(await History(factory));
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/LoginHistory")).StatusCode);

        await AccountFlowTests.Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        await CreateStaff(admin, factory, "history-staff@example.test");
        var credentials = Assert.Single(factory.Email.TemporaryCredentials);
        await AccountFlowTests.Post(staff, "/Account/Login", new()
        {
            ["Email"] = credentials.Email, ["Password"] = credentials.Password
        });
        Assert.Single(await History(factory));

        await AccountFlowTests.Post(staff, "/Account/ChangeTemporaryPassword", new()
        {
            ["Password"] = "Private-History-2026!", ["ConfirmPassword"] = "Private-History-2026!"
        });
        Assert.Equal(2, (await History(factory)).Count);
        var denied = await staff.GetAsync("/LoginHistory");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("https://localhost/Account/AccessDenied", denied.Headers.Location!.AbsoluteUri);
        var emptySearch = await admin.GetStringAsync("/LoginHistory?search=missing-person");
        Assert.Contains("No login history found.", emptySearch);
    }

    [Fact]
    public async Task Expiry_closes_at_the_expiry_time_without_inventing_a_logout()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);

        var now = DateTime.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
            await database.ExecuteAsync("UPDATE dbo.tblUserLoginHistory SET LoginAtUtc=@login,LastSeenAtUtc=@seen,ExpiresAtUtc=@expiry",
                new { login = now.AddHours(-1), seen = now.AddMinutes(-40), expiry = now.AddMinutes(-30) });
            var repository = scope.ServiceProvider.GetRequiredService<ILoginHistoryRepository>();
            await repository.CloseExpiredSessionsAsync(now);
            await repository.CloseExpiredSessionsAsync(now);
        }

        var expired = Assert.Single(await History(factory));
        Assert.Equal(LoginHistoryConstants.Expired, expired.EndReason);
        Assert.Equal(expired.ExpiresAtUtc, expired.EndedAtUtc);
        Assert.Null(expired.LogoutAtUtc);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_ends_all_sessions_for_that_user()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var first = factory.Client();
        using var second = factory.Client();
        using var recovery = factory.Client();
        await AccountFlowTests.Login(first, "admin@example.test", SchoolFactory.AdminPassword);
        await AccountFlowTests.Login(second, "admin@example.test", SchoolFactory.AdminPassword);
        await AccountFlowTests.Post(recovery, "/Account/ForgotPassword", new() { ["Email"] = "admin@example.test" });

        var address = new Uri(Assert.Single(factory.Email.Links).Url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(address.Query);
        var reset = await AccountFlowTests.Post(recovery, "/Account/ResetPassword", new()
        {
            ["UserId"] = query["userId"].ToString(),
            ["Code"] = query["code"].ToString(),
            ["Password"] = "Reset-History-2026!",
            ["ConfirmPassword"] = "Reset-History-2026!"
        }, address.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);

        var sessions = await History(factory);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session =>
        {
            Assert.Equal(LoginHistoryConstants.PasswordReset, session.EndReason);
            Assert.NotNull(session.EndedAtUtc);
            Assert.Null(session.LogoutAtUtc);
        });
        Assert.Equal(HttpStatusCode.Redirect, (await first.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await second.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_closes_their_sessions_without_closing_the_administrator_session()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var admin = factory.Client();
        using var first = factory.Client();
        using var second = factory.Client();
        await AccountFlowTests.Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        await CreateStaff(admin, factory, "disabled-history@example.test");
        var credentials = Assert.Single(factory.Email.TemporaryCredentials);
        await AccountFlowTests.Post(first, "/Account/Login", new()
        {
            ["Email"] = credentials.Email, ["Password"] = credentials.Password
        });
        await AccountFlowTests.Post(first, "/Account/ChangeTemporaryPassword", new()
        {
            ["Password"] = "Private-History-2026!", ["ConfirmPassword"] = "Private-History-2026!"
        });
        await AccountFlowTests.Login(second, credentials.Email, "Private-History-2026!");

        string staffId;
        byte[] version;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
            var staff = (await database.UsersAsync()).Single(user => user.Email == credentials.Email);
            staffId = staff.Id;
            version = staff.RowVersion;
        }

        await AccountFlowTests.Post(admin, "/Users/SetActive", new()
        {
            ["id"] = staffId, ["active"] = "false", ["rowVersion"] = Convert.ToBase64String(version)
        }, "/Users");

        var sessions = await History(factory);
        Assert.Equal(2, sessions.Count(session => session.UserId == staffId));
        Assert.All(sessions.Where(session => session.UserId == staffId), session =>
        {
            Assert.Equal(LoginHistoryConstants.AccountDisabled, session.EndReason);
            Assert.Null(session.LogoutAtUtc);
        });
        Assert.Null(Assert.Single(sessions, session => session.UserId != staffId).EndedAtUtc);
        Assert.Equal(HttpStatusCode.Redirect, (await first.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await second.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/LoginHistory")).StatusCode);
    }

    [Fact]
    public async Task Security_stamp_rejection_records_revocation()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("admin@example.test"))!;
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
        var session = Assert.Single(await History(factory));
        Assert.Equal(LoginHistoryConstants.Revoked, session.EndReason);
        Assert.Null(session.LogoutAtUtc);
        Assert.NotNull(session.EndedAtUtc);
    }

    private static async Task<List<UserLoginHistory>> History(SchoolFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        return await database.HistoryAsync();
    }

    private static async Task CreateStaff(HttpClient admin, SchoolFactory factory, string email)
    {
        var response = await AccountFlowTests.Post(admin, "/Users/Create", new()
        {
            ["FullName"] = "History Staff", ["Email"] = email,
            ["DesignationId"] = factory.StaffDesignationId.ToString()
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
