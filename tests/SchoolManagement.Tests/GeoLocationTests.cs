using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;
using SchoolManagement.Services;
using Xunit;

namespace SchoolManagement.Tests;

public class GeoLocationTests
{
    private const string Address = "Malad, Rajan Pada, Mindspace, Malad West, Mumbai, Maharashtra 400064, India";
    private const string TestKey = "unit-test-api-key";

    [Theory]
    [InlineData("formattedAddress")]
    [InlineData("formatted_address")]
    public async Task Address_response_and_device_form_factor_are_captured(string addressProperty)
    {
        var json = JsonSerializer.Serialize(new { results = new[] { new Dictionary<string, string> { [addressProperty] = Address } } });
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        using var client = new HttpClient(handler);
        var service = Service(client, TestKey);
        var session = Location();
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await service.ApplyAsync(session);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        Assert.Equal(Address, session.GeoLocation);
        Assert.Equal(json, session.LocationResponse);
        Assert.Equal("Desktop", session.DeviceFormFactor);
        Assert.Equal("Google Maps", session.GeocodingProvider);
        Assert.Equal("Resolved", session.GeocodingStatus);
        Assert.Contains("19.18,72.83", handler.Url);
        Assert.DoesNotContain(TestKey, handler.Url);
        Assert.Equal(TestKey, handler.KeyHeader);
    }

    [Fact]
    public async Task No_key_keeps_an_explicitly_approximate_offline_address_without_http_calls()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("No request expected"));
        using var client = new HttpClient(handler);
        var session = Location();
        await Service(client, "").ApplyAsync(session);
        Assert.Equal(0, handler.Calls);
        Assert.Equal("Near Malad, Maharashtra, India", session.GeoLocation);
        Assert.Equal("GeoNames", session.GeocodingProvider);
        Assert.Equal("NotConfigured", session.GeocodingStatus);
        using var json = JsonDocument.Parse(session.LocationResponse!);
        Assert.True(json.RootElement.GetProperty("approximate").GetBoolean());
    }

    [Theory]
    [InlineData("Denied")]
    [InlineData("Stale")]
    [InlineData("Invalid")]
    public async Task Rejected_browser_location_never_contacts_the_provider(string status)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("No request expected"));
        using var client = new HttpClient(handler);
        var session = Location();
        session.LocationStatus = status;
        await Service(client, TestKey).ApplyAsync(session);
        Assert.Equal(0, handler.Calls);
        Assert.Null(session.GeoLocation);
        Assert.Null(session.LocationResponse);
        Assert.Equal("NotShared", session.GeocodingStatus);
        Assert.Equal("Desktop", session.DeviceFormFactor);
    }

    [Theory]
    [InlineData("forbidden", "HttpError")]
    [InlineData("timeout", "Timeout")]
    [InlineData("network", "Unavailable")]
    [InlineData("invalid-json", "Unavailable")]
    [InlineData("no-results", "NoResult")]
    [InlineData("invalid-shape", "InvalidResponse")]
    [InlineData("too-large", "ResponseTooLarge")]
    public async Task Provider_failure_keeps_the_offline_address_and_does_not_fail_login(string failure, string expected)
    {
        var handler = new StubHandler(_ =>
        {
            if (failure == "timeout") throw new OperationCanceledException();
            if (failure == "network") throw new HttpRequestException();
            var payload = failure switch
            {
                "invalid-json" => "not json",
                "no-results" => "{\"results\":[]}",
                "invalid-shape" => "{\"results\":[{\"formattedAddress\":42}]}",
                "too-large" => new string('x', 262145),
                _ => "{}"
            };
            return new HttpResponseMessage(failure == "forbidden" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            };
        });
        using var client = new HttpClient(handler);
        var session = Location();
        await Service(client, TestKey).ApplyAsync(session);
        Assert.Equal(expected, session.GeocodingStatus);
        Assert.Equal("Near Malad, Maharashtra, India", session.GeoLocation);
        Assert.Equal("GeoNames", session.GeocodingProvider);
    }

    [Fact]
    public async Task Google_address_and_large_json_are_saved_through_dapper_and_encoded_in_the_grid()
    {
        await using var factory = new GoogleFactory();
        await factory.Initialize();
        using var client = factory.Client();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/143.0.0.0");
        var response = await AccountFlowTests.Post(client, "/Account/Login", new()
        {
            ["Email"] = "admin@example.test", ["Password"] = SchoolFactory.AdminPassword,
            ["Device.LocationStatus"] = "Granted", ["Device.Latitude"] = "19.18", ["Device.Longitude"] = "72.83",
            ["Device.AccuracyMeters"] = "89",
            ["Device.CapturedAtUnixMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        var session = (await database.HistoryAsync()).Single();
        Assert.Equal(Address, session.GeoLocation);
        Assert.Equal("Desktop", session.DeviceFormFactor);
        Assert.Equal(GoogleFactory.Payload, session.LocationResponse);
        Assert.Equal("Resolved", session.GeocodingStatus);
        var html = await client.GetStringAsync("/LoginHistory");
        Assert.Contains(Address, html);
        Assert.Contains("LocationResponse (JSON)", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain(TestKey, html);
        await AccountFlowTests.Post(client, "/Account/Logout", new(), "/");
        var closed = (await database.HistoryAsync()).Single();
        Assert.Equal(session.GeoLocation, closed.GeoLocation);
        Assert.Equal(session.LocationResponse, closed.LocationResponse);
    }

    [Fact]
    public async Task Upgrade_adds_geo_columns_without_changing_existing_rows_and_is_repeatable()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        Assert.StartsWith("SchoolManagementTests_", database.Name);
        var original = (await database.HistoryAsync()).Single();
        await database.ExecuteAsync("""
            ALTER TABLE dbo.tblUserLoginHistory DROP COLUMN
                GeoLocation, DeviceFormFactor, LocationResponse, GeocodingProvider, GeocodingStatus;
            """);
        await StoredProcedureSetup.ApplyAsync(database);
        await StoredProcedureSetup.ApplyAsync(database);
        var retained = (await database.HistoryAsync()).Single();
        Assert.Equal(original.SessionId, retained.SessionId);
        Assert.Equal(original.LoginAtUtc, retained.LoginAtUtc);
        Assert.Null(retained.GeoLocation);
        Assert.Null(retained.LocationResponse);
    }

    private static GeoLocationService Service(HttpClient client, string key) =>
        new(client, Options.Create(new GeocodingSettings { ApiKey = key }), NullLogger<GeoLocationService>.Instance);

    private static UserLoginHistory Location() => new()
    {
        Latitude = 19.18m, Longitude = 72.83m, LocationStatus = "Granted", LocationSource = "BrowserGeolocation",
        DeviceType = "Desktop", AreaName = "Malad", AreaRegion = "Maharashtra", AreaCountry = "India"
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Url { get; private set; } = "";
        public string KeyHeader { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.AbsoluteUri;
            KeyHeader = request.Headers.GetValues("X-Goog-Api-Key").Single();
            return Task.FromResult(reply(request));
        }
    }

    private sealed class GoogleFactory : SchoolFactory
    {
        public static readonly string Payload = JsonSerializer.Serialize(new
        {
            results = new[] { new { formattedAddress = Address } },
            extra = "<script>alert(1)</script>" + new string('x', 5000)
        });
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.Configure<GeocodingSettings>(options => options.ApiKey = TestKey);
                services.AddHttpClient<IGeoLocationService, GeoLocationService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(Payload, Encoding.UTF8, "application/json") }));
            });
        }
    }
}
