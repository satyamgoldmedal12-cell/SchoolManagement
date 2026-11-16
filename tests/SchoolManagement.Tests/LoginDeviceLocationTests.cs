using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Data;
using SchoolManagement.Models;
using Xunit;

namespace SchoolManagement.Tests;

public class LoginDeviceLocationTests
{
    private const string ChromeWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/143.0.0.0 Safari/537.36";

    [Fact]
    public async Task Login_records_device_location_and_preserves_the_login_snapshot()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ChromeWindows);
        var loginPage = await client.GetStringAsync("/Account/Login");
        Assert.Contains("name=\"Device.Latitude\"", loginPage);
        Assert.DoesNotContain("Share login location", loginPage);
        Assert.DoesNotContain("System name", loginPage);
        Assert.DoesNotContain("name=\"Device.DeviceName\"", loginPage);
        Assert.Contains("Your browser will ask", loginPage);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/js/login-device.js")).StatusCode);

        var fields = LoginFields();
        fields["Device.DeviceName"] = "Office <img src=x onerror=alert(1)>";
        var response = await AccountFlowTests.Post(client, "/Account/Login", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var original = Assert.Single(await History(factory));
        Assert.NotNull(original.DeviceId);
        Assert.Equal("Windows", original.OperatingSystem);
        Assert.Equal("Chrome 143.0.0.0", original.Browser);
        Assert.Equal("Desktop", original.DeviceType);
        Assert.Equal("Asia/Kolkata", original.TimeZone);
        Assert.Equal("BrowserGeolocation", original.LocationSource);
        Assert.Equal(19.076m, original.Latitude);
        Assert.Equal(72.8777m, original.Longitude);
        Assert.False(string.IsNullOrWhiteSpace(original.AreaName));
        Assert.Equal("Maharashtra", original.AreaRegion);
        Assert.Equal("India", original.AreaCountry);
        Assert.Equal("GeoNamesNearestPlace", original.AreaSource);
        Assert.InRange(original.AreaDistanceKm!.Value, 0, 25);

        var page = await client.GetStringAsync("/LoginHistory");
        Assert.Contains("19.076000", page);
        Assert.Contains("View map", page);
        Assert.Contains("Approximate nearest place", page);
        Assert.Contains("Office &lt;img", page);
        Assert.DoesNotContain("Office <img", page);

        client.DefaultRequestHeaders.Remove("User-Agent");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64) Firefox/140.0");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        var unchanged = Assert.Single(await History(factory));
        Assert.Equal(original.Browser, unchanged.Browser);
        Assert.Equal(original.Latitude, unchanged.Latitude);
        Assert.Equal(original.AreaName, unchanged.AreaName);

        await AccountFlowTests.Post(client, "/Account/Logout", new(), "/");
        await AccountFlowTests.Post(client, "/Account/Login", new()
        {
            ["Email"] = "admin@example.test", ["Password"] = SchoolFactory.AdminPassword,
            ["Device.LocationStatus"] = "Denied"
        });
        var sessions = await History(factory);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(original.DeviceId, sessions[1].DeviceId);
        Assert.Equal("Linux", sessions[1].OperatingSystem);
        Assert.Equal("Denied", sessions[1].LocationStatus);
        Assert.Null(sessions[1].Latitude);
        Assert.Null(sessions[1].AreaName);

        using var anotherBrowser = factory.Client();
        await AccountFlowTests.Login(anotherBrowser, "admin@example.test", SchoolFactory.AdminPassword);
        var last = (await History(factory)).Last();
        Assert.NotEqual(original.DeviceId, last.DeviceId);
        Assert.Null(last.Latitude);
    }

    [Fact]
    public async Task Location_from_temporary_login_survives_first_password_setup()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var admin = factory.Client();
        using var staff = factory.Client();
        staff.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ChromeWindows);
        await AccountFlowTests.Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        await AccountFlowTests.Post(admin, "/Users/Create", new()
        {
            ["FullName"] = "Location Staff", ["Email"] = "location-staff@example.test",
            ["DesignationId"] = factory.StaffDesignationId.ToString()
        });
        var credentials = Assert.Single(factory.Email.TemporaryCredentials);
        var fields = LoginFields();
        fields["Email"] = credentials.Email;
        fields["Password"] = credentials.Password;
        fields["Device.DeviceName"] = "Reception laptop";
        var setup = await AccountFlowTests.Post(staff, "/Account/Login", fields);
        Assert.Equal("/Account/ChangeTemporaryPassword", setup.Headers.Location?.OriginalString);
        Assert.Single(await History(factory));

        var setupPage = await staff.GetStringAsync("/Account/ChangeTemporaryPassword");
        Assert.DoesNotContain("name=\"Device.DeviceName\"", setupPage);
        Assert.DoesNotContain("Share login location", setupPage);
        Assert.Contains("value=\"19.076\"", setupPage);

        var changed = await AccountFlowTests.Post(staff, "/Account/ChangeTemporaryPassword", new()
        {
            ["Password"] = "Private-Location-2026!", ["ConfirmPassword"] = "Private-Location-2026!"
        });
        Assert.Equal("/", changed.Headers.Location?.OriginalString);
        var session = (await History(factory)).Last();
        Assert.Equal("Reception laptop", session.DeviceName);
        Assert.Equal("Granted", session.LocationStatus);
        Assert.Equal(19.076m, session.Latitude);
        Assert.Equal("Windows", session.OperatingSystem);
        Assert.Equal("India", session.AreaCountry);
    }

    [Fact]
    public async Task User_can_remove_pending_location_before_finishing_first_password_setup()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var admin = factory.Client();
        using var staff = factory.Client();
        await AccountFlowTests.Login(admin, "admin@example.test", SchoolFactory.AdminPassword);
        await AccountFlowTests.Post(admin, "/Users/Create", new()
        {
            ["FullName"] = "Location Removal", ["Email"] = "remove-location@example.test",
            ["DesignationId"] = factory.StaffDesignationId.ToString()
        });
        var credentials = Assert.Single(factory.Email.TemporaryCredentials);
        var fields = LoginFields();
        fields["Email"] = credentials.Email;
        fields["Password"] = credentials.Password;
        await AccountFlowTests.Post(staff, "/Account/Login", fields);
        var changed = await AccountFlowTests.Post(staff, "/Account/ChangeTemporaryPassword", new()
        {
            ["Password"] = "Remove-Location-2026!", ["ConfirmPassword"] = "Remove-Location-2026!",
            ["Device.LocationStatus"] = "NotShared"
        });
        Assert.Equal("/", changed.Headers.Location?.OriginalString);
        var session = (await History(factory)).Last();
        Assert.Equal("NotShared", session.LocationStatus);
        Assert.Null(session.Latitude);
        Assert.Null(session.Longitude);
        Assert.Null(session.LocationCapturedAtUtc);
    }

    [Fact]
    public async Task Metadata_upgrade_preserves_existing_login_rows_and_is_repeatable()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        Assert.StartsWith("SchoolManagementTests_", database.Name);
        var user = (await database.UsersAsync()).Single();
        Assert.Empty(await database.HistoryAsync());
        await database.ExecuteAsync("DROP TABLE dbo.tblUserLoginHistory;");
        var legacyScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LoginHistoryV1.sql"));
        await database.ExecuteAsync(legacyScript);
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await database.ExecuteAsync("""
            INSERT INTO dbo.tblUserLoginHistory
                (SessionId, UserId, LoginAtUtc, LastSeenAtUtc, ExpiresAtUtc, IpAddress, UserAgent)
            VALUES (@sessionId, @userId, @now, @now, @expiry, N'192.168.1.10', N'Legacy browser');
            """, new { sessionId, userId = user.Id, now, expiry = now.AddMinutes(30) });

        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "20261031_LoginDeviceLocation.sql"));
        await database.ExecuteAsync(script);
        await database.ExecuteAsync(script);
        await StoredProcedureSetup.ApplyAsync(database);

        var retained = (await database.HistoryAsync()).Single();
        Assert.Equal(sessionId, retained.SessionId);
        Assert.Equal(user.Id, retained.UserId);
        Assert.Equal("192.168.1.10", retained.IpAddress);
        Assert.Equal("Legacy browser", retained.UserAgent);
        Assert.Null(retained.DeviceId);
        Assert.Null(retained.Latitude);
        Assert.Null(retained.LocationStatus);
        Assert.Equal(user.PasswordHash, ((await database.UsersAsync()).Single()).PasswordHash);

        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        Assert.Equal(2, (await database.HistoryAsync()).Count);
        Assert.NotNull(((await database.HistoryAsync()).OrderByDescending(session => session.Id).First()).DeviceId);
    }

    private static Dictionary<string, string> LoginFields()
    {
        return new Dictionary<string, string>
        {
            ["Email"] = "admin@example.test", ["Password"] = SchoolFactory.AdminPassword,
            ["Device.TimeZone"] = "Asia/Kolkata", ["Device.LocationStatus"] = "Granted",
            ["Device.Latitude"] = "19.076", ["Device.Longitude"] = "72.8777", ["Device.AccuracyMeters"] = "25.5",
            ["Device.CapturedAtUnixMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
        };
    }

    private static async Task<List<UserLoginHistory>> History(SchoolFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestDatabase>().HistoryAsync();
    }
}
