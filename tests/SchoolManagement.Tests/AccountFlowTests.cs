using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Data;
using SchoolManagement.Models;
using Xunit;

namespace SchoolManagement.Tests;

public class AccountFlowTests
{
    [Fact]
    public async Task Registration_emails_temporary_password_and_blocks_app_until_first_change()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var admin = factory.Client(); using var user = factory.Client();
        await Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        await CreateUser(admin, factory, "teacher@example.test");
        var message = Assert.Single(factory.Email.TemporaryCredentials);
        Assert.Equal("teacher@example.test", message.Email);
        Assert.Equal("https://school.example.test/Account/Login", message.LoginUrl);
        Assert.True(message.ExpiresAt > DateTime.UtcNow.AddHours(23));
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var row = (await manager.FindByEmailAsync(message.Email))!;
            Assert.True(row.MustChangePassword); Assert.Null(row.PasswordSetAt); Assert.NotNull(row.InvitationSentAt);
            Assert.NotEqual(message.Password, row.PasswordHash);
            Assert.True(await manager.CheckPasswordAsync(row, message.Password));
            Assert.True(await manager.IsInRoleAsync(row, "Staff"));
        }
        var signedIn = await Post(user, "/Account/Login", new() { ["Email"] = message.Email, ["Password"] = message.Password });
        Assert.Equal("/Account/ChangeTemporaryPassword", signedIn.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await user.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await user.GetAsync("/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/Account/ChangeTemporaryPassword", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        var same = await Post(user, "/Account/ChangeTemporaryPassword", new() { ["Password"] = message.Password, ["ConfirmPassword"] = message.Password });
        Assert.Contains("different from your temporary password", await same.Content.ReadAsStringAsync());
        var weak = await Post(user, "/Account/ChangeTemporaryPassword", new() { ["Password"] = "weak", ["ConfirmPassword"] = "weak" });
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
        var changed = await Post(user, "/Account/ChangeTemporaryPassword", new() { ["Password"] = "My-Private-2026!", ["ConfirmPassword"] = "My-Private-2026!" });
        Assert.Equal("/", changed.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var row = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(message.Email))!;
            Assert.False(row.MustChangePassword); Assert.Null(row.TemporaryPasswordExpiresAt); Assert.NotNull(row.PasswordSetAt);
        }
        var list = await admin.GetStringAsync("/api/users?search=teacher%40example.test");
        Assert.Contains("teacher@example.test", list); Assert.DoesNotContain(message.Password, list); Assert.DoesNotContain("passwordHash", list);
        Assert.Contains("users-workspace", await admin.GetStringAsync("/Users"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/dist/school.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/dist/school.css")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Email_failure_retains_account_and_resend_replaces_temporary_credentials()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var admin = factory.Client(); await Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        factory.Email.Fail = true;
        await CreateUser(admin, factory, "pending@example.test");
        var page = await admin.GetStringAsync("/Users");
        Assert.Contains("email delivery failed", page);
        string id;
        using (var scope = factory.Services.CreateScope())
        {
            var row = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("pending@example.test"))!;
            id = row.Id; Assert.True(row.MustChangePassword); Assert.NotNull(row.PasswordHash); Assert.Null(row.InvitationSentAt);
        }
        factory.Email.Fail = false;
        await Post(admin, "/Users/SendPasswordLink", new() { ["id"] = id }, "/Users");
        var oldPassword = Assert.Single(factory.Email.TemporaryCredentials).Password;
        using var partial = factory.Client();
        var response = await Post(partial, "/Account/Login", new() { ["Email"] = "pending@example.test", ["Password"] = oldPassword });
        Assert.Equal("/Account/ChangeTemporaryPassword", response.Headers.Location?.OriginalString);
        await Post(admin, "/Users/SendPasswordLink", new() { ["id"] = id }, "/Users");
        var newPassword = factory.Email.TemporaryCredentials.Last().Password;
        Assert.NotEqual(oldPassword, newPassword);
        var staleSession = await Post(partial, "/Account/ChangeTemporaryPassword", new() { ["Password"] = "Private-Pending-2026!", ["ConfirmPassword"] = "Private-Pending-2026!" });
        Assert.Contains("session is invalid or expired", await staleSession.Content.ReadAsStringAsync());
        using var anonymous = factory.Client();
        var old = await Post(anonymous, "/Account/Login", new() { ["Email"] = "pending@example.test", ["Password"] = oldPassword });
        Assert.Contains("Invalid email or password", await old.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TestDatabase>().ExecuteAsync(
                "UPDATE dbo.tblUsers SET TemporaryPasswordExpiresAt=@expiry WHERE Id=@id", new { id, expiry = DateTime.UtcNow.AddMinutes(-1) });
        var expired = await Post(anonymous, "/Account/Login", new() { ["Email"] = "pending@example.test", ["Password"] = newPassword });
        Assert.Contains("temporary password has expired", await expired.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Recovery_is_single_use_invalidates_sessions_and_cannot_reset_another_user()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var admin = factory.Client(); await Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        using var anonymous = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = "admin@example.test" });
        var resetUrl = Assert.Single(factory.Email.Links).Url;
        var address = new Uri(resetUrl); var query = QueryHelpers.ParseQuery(address.Query);
        var forged = await Post(anonymous, "/Account/ResetPassword", new()
        {
            ["UserId"] = "another-user", ["Code"] = query["code"].ToString(), ["Password"] = "New-Admin-2026!", ["ConfirmPassword"] = "New-Admin-2026!"
        }, address.PathAndQuery);
        Assert.Contains("invalid or expired", await forged.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await Reset(anonymous, resetUrl, "New-Admin-2026!")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/")).StatusCode);
        Assert.Contains("invalid or expired", await (await Reset(anonymous, resetUrl, "Other-Admin-2026!")).Content.ReadAsStringAsync());
        await Login(admin, "admin@example.test", "New-Admin-2026!");
        var known = await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = "admin@example.test" });
        var unknown = await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = "unknown@example.test" });
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan = TimeSpan.FromSeconds(-1);
        Assert.Contains("invalid or expired", await (await Reset(anonymous, factory.Email.Links.Last().Url, "Expired-Admin-2026!")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Catalog_and_Dapper_search_obey_roles_and_optimistic_concurrency()
    {
        await using var factory = new SchoolFactory(); await factory.Initialize();
        using var admin = factory.Client(); await Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/Roles/Create", new() { ["Name"] = "Teacher" })).StatusCode);
        string roleId;
        using (var scope = factory.Services.CreateScope()) roleId = (await scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>().FindByNameAsync("Teacher"))!.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/Designations/Create", new() { ["DesignationName"] = "Class Teacher", ["RoleId"] = roleId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Designations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Roles")).StatusCode);
        var duplicate = await Post(admin, "/Designations/Create", new() { ["DesignationName"] = "Class Teacher", ["RoleId"] = roleId });
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());
        await CreateUser(admin, factory, "active@example.test");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDatabase>();
            var row = (await db.UsersAsync()).Single(x => x.Email == "active@example.test");
            var version = Convert.ToBase64String(row.RowVersion);
            await Post(admin, "/Users/SetActive", new() { ["id"] = row.Id, ["active"] = "false", ["rowVersion"] = version }, "/Users");
            await Post(admin, "/Users/SetActive", new() { ["id"] = row.Id, ["active"] = "true", ["rowVersion"] = version }, "/Users");
            Assert.Contains("changed in another session", await admin.GetStringAsync("/Users"));
            Assert.False((await db.UsersAsync()).Single(x => x.Id == row.Id).IsActive);
        }
        var injection = await admin.GetStringAsync("/api/users?search=" + Uri.EscapeDataString("'; DROP TABLE tblUsers;--"));
        Assert.Contains("\"items\":[]", injection);
        Assert.Contains("admin@example.test", await admin.GetStringAsync("/api/users?search=admin"));
        using var anonymous = factory.Client();
        for (var attempt = 0; attempt < 11; attempt++) await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = "unknown@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(anonymous, "/Account/ForgotPassword", new() { ["Email"] = "unknown@example.test" })).StatusCode);
    }
    private static async Task CreateUser(HttpClient admin, SchoolFactory factory, string email)
    {
        var response = await Post(admin, "/Users/Create", new() { ["FullName"] = "School User", ["Email"] = email, ["DesignationId"] = factory.StaffDesignationId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    internal static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields, string? formPath = null)
    {
        var html = await client.GetStringAsync(formPath ?? path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Form needs an antiforgery token.");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
    internal static async Task Login(HttpClient client, string email, string password)
    {
        var response = await Post(client, "/Account/Login", new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/", response.Headers.Location?.OriginalString);
    }
    private static Task<HttpResponseMessage> Reset(HttpClient client, string url, string password)
    {
        var address = new Uri(url); var query = QueryHelpers.ParseQuery(address.Query);
        return Post(client, "/Account/ResetPassword", new() { ["UserId"] = query["userId"].ToString(), ["Code"] = query["code"].ToString(), ["Password"] = password, ["ConfirmPassword"] = password }, address.PathAndQuery);
    }
}

public class CaptureEmailSender : IEmailSender
{
    public List<(string Email, string Url)> Links { get; } = [];
    public List<(string Email, string Password, string LoginUrl, DateTime ExpiresAt)> TemporaryCredentials { get; } = [];
    public bool Fail { get; set; }
    public Task SendPasswordLinkAsync(string email, string name, string url, bool invitation)
    {
        if (Fail) throw new InvalidOperationException("Simulated SMTP failure");
        Links.Add((email, url)); return Task.CompletedTask;
    }
    public Task SendTemporaryCredentialsAsync(string email, string name, string password, string loginUrl, DateTime expiresAt)
    {
        if (Fail) throw new InvalidOperationException("Simulated SMTP failure");
        TemporaryCredentials.Add((email, password, loginUrl, expiresAt)); return Task.CompletedTask;
    }
}
public class SchoolFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Admin-Test-2026!";
    public CaptureEmailSender Email { get; } = new();
    public int StaffDesignationId { get; private set; }
    private readonly string databaseName = "SchoolManagementTests_" + Guid.NewGuid().ToString("N");
    private string Connection => new SqlConnectionStringBuilder
    {
        DataSource = Environment.GetEnvironmentVariable("SCHOOLMANAGEMENT_TEST_SQL_SERVER") ?? "(localdb)\\SchoolManagementTests15",
        InitialCatalog = databaseName, IntegratedSecurity = true, TrustServerCertificate = true
    }.ToString();
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("environment", "Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:SchoolManagement"] = Connection, ["Application:PublicBaseUrl"] = "https://school.example.test",
          ["Geocoding:ApiKey"] = "" }));
        builder.ConfigureServices(services =>
        {
            services.AddScoped<TestDatabase>(provider => new TestDatabase(provider.GetRequiredService<SqlConnectionFactory>()));
            services.RemoveAll<IEmailSender>(); services.AddSingleton<IEmailSender>(Email);
        });
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    public async Task Initialize()
    {
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        // All SQL fixtures and application stores must use this isolated database.
        await using var sql = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>().CreateConnection();
        Assert.Equal(databaseName, sql.Database);
        Assert.Equal(databaseName, db.Name); await db.EnsureCreatedAsync();
        await StoredProcedureSetup.ApplyAsync(db);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var adminRole = new ApplicationRole { Name = AppRoles.Administrator }; var staffRole = new ApplicationRole { Name = "Staff" };
        Assert.True((await roles.CreateAsync(adminRole)).Succeeded); Assert.True((await roles.CreateAsync(staffRole)).Succeeded);
        var adminDesignation = new Designation { DesignationName = "Administrator", RoleId = adminRole.Id };
        var staffDesignation = new Designation { DesignationName = "Staff", RoleId = staffRole.Id };
        await db.InsertDesignationAsync(adminDesignation); await db.InsertDesignationAsync(staffDesignation);
        StaffDesignationId = staffDesignation.DesignationId;
        var admin = new ApplicationUser { UserName = "admin@example.test", Email = "admin@example.test", FullName = "Test Administrator", DesignationId = adminDesignation.DesignationId };
        Assert.True((await users.CreateAsync(admin, AdminPassword)).Succeeded); Assert.True((await users.AddToRoleAsync(admin, AppRoles.Administrator)).Succeeded);
    }
    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDatabase>(); Assert.StartsWith("SchoolManagementTests_", db.Name);
            await db.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }
}
