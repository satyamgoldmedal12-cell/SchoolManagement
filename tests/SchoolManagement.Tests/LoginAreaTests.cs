using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;
using Xunit;

namespace SchoolManagement.Tests;

public class LoginAreaTests
{
    [Fact]
    public void Nearest_place_is_selected_and_provenance_is_stored()
    {
        using var data = new AreaData("1\tFar\t10.1\t20\tRegion\tCountry\n2\tNearest\t10\t20\tRegion\tCountry\n");
        var session = Location(10, 20);
        data.Service.ApplyTo(session);
        Assert.Equal("Nearest", session.AreaName);
        Assert.Equal("Region", session.AreaRegion);
        Assert.Equal("Country", session.AreaCountry);
        Assert.Equal(2, session.AreaGeoNamesId);
        Assert.Equal(0m, session.AreaDistanceKm);
        Assert.Equal("GeoNamesNearestPlace", session.AreaSource);
    }

    [Theory]
    [InlineData("Denied")]
    [InlineData("Invalid")]
    [InlineData("Stale")]
    public void Unaccepted_coordinates_cannot_store_an_area(string status)
    {
        using var data = new AreaData("1\tPlace\t10\t20\tRegion\tCountry\n");
        var session = Location(10, 20);
        session.LocationStatus = status;
        session.AreaName = "Untrusted previous value";
        data.Service.ApplyTo(session);
        Assert.Null(session.AreaName);
        Assert.Null(session.AreaGeoNamesId);
        Assert.Equal("NotShared", session.AreaSource);
    }

    [Fact]
    public void Faraway_points_are_not_presented_as_the_users_area()
    {
        using var data = new AreaData("1\tFar\t11\t20\tRegion\tCountry\n");
        var session = Location(10, 20);
        data.Service.ApplyTo(session);
        Assert.Null(session.AreaName);
        Assert.Equal("NoNearbyPlace", session.AreaSource);
    }

    [Fact]
    public void Nearest_point_can_be_found_across_the_international_date_line()
    {
        using var data = new AreaData("1\tNearby\t10\t179.99\tRegion\tCountry\n");
        var session = Location(10, -179.99m);
        data.Service.ApplyTo(session);
        Assert.Equal("Nearby", session.AreaName);
        Assert.InRange(session.AreaDistanceKm!.Value, 2, 3);
    }

    [Fact]
    public void Missing_or_corrupt_data_does_not_break_login()
    {
        using var data = new AreaData(null);
        var session = Location(10, 20);
        data.Service.ApplyTo(session);
        Assert.Null(session.AreaName);
        Assert.Equal("Unavailable", session.AreaSource);
        File.WriteAllText(data.FilePath, "not gzip");
        var service = data.NewService();
        service.ApplyTo(session);
        Assert.Null(session.AreaName);
        Assert.Equal("Unavailable", session.AreaSource);
    }

    [Fact]
    public async Task Area_upgrade_keeps_existing_users_and_history_and_can_be_run_twice()
    {
        await using var factory = new SchoolFactory();
        await factory.Initialize();
        using var client = factory.Client();
        await AccountFlowTests.Login(client, "admin@example.test", SchoolFactory.AdminPassword);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDatabase>();
        Assert.StartsWith("SchoolManagementTests_", database.Name);
        var user = (await database.UsersAsync()).Single();
        var session = (await database.HistoryAsync()).Single();
        await database.ExecuteAsync("""
            ALTER TABLE dbo.tblUserLoginHistory DROP COLUMN
                AreaName, AreaRegion, AreaCountry, AreaSource, AreaGeoNamesId, AreaDistanceKm;
            """);
        await StoredProcedureSetup.ApplyAsync(database);
        await StoredProcedureSetup.ApplyAsync(database);
        var retained = (await database.HistoryAsync()).Single();
        Assert.Equal(session.Id, retained.Id);
        Assert.Equal(session.SessionId, retained.SessionId);
        Assert.Equal(session.UserId, retained.UserId);
        Assert.Equal(session.LoginAtUtc, retained.LoginAtUtc);
        Assert.Null(retained.AreaName);
        Assert.Null(retained.AreaSource);
        Assert.Equal(user.PasswordHash, ((await database.UsersAsync()).Single()).PasswordHash);
    }

    private static UserLoginHistory Location(decimal latitude, decimal longitude)
    {
        return new UserLoginHistory
        {
            Latitude = latitude, Longitude = longitude,
            LocationStatus = "Granted", LocationSource = "BrowserGeolocation"
        };
    }

    private sealed class AreaData : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SchoolManagementAreaTests_" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(_root, "Data", "Locations", "places.tsv.gz");
        public LoginAreaService Service { get; }

        public AreaData(string? text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (text is not null)
            {
                using var file = File.Create(FilePath);
                using var gzip = new GZipStream(file, CompressionMode.Compress);
                using var writer = new StreamWriter(gzip);
                writer.Write(text);
            }
            Service = NewService();
        }

        public LoginAreaService NewService()
        {
            return new LoginAreaService(new AreaEnvironment { ContentRootPath = _root }, NullLogger<LoginAreaService>.Instance);
        }

        public void Dispose()
        {
            File.Delete(FilePath);
            Directory.Delete(Path.GetDirectoryName(FilePath)!);
            Directory.Delete(Path.Combine(_root, "Data"));
            Directory.Delete(_root);
        }
    }

    private sealed class AreaEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SchoolManagement.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
