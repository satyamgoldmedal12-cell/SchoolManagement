using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using SchoolManagement.Models;
using SchoolManagement.Services;
using Xunit;

namespace SchoolManagement.Tests;

public class LoginMetadataTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/143.0.0.0 Safari/537.36", "Windows", "Chrome 143.0.0.0", "Desktop")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.1.0", "Windows", "Edge 143.0.1.0", "Desktop")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Version/18.0 Safari/605.1.15", "macOS", "Safari 18.0", "Desktop")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) CriOS/143.0.0.0 Mobile/15E148 Safari/604.1", "iOS / iPadOS", "Chrome 143.0.0.0", "Mobile")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) Chrome/143.0.0.0 Mobile Safari/537.36", "Android", "Chrome 143.0.0.0", "Mobile")]
    [InlineData("Mozilla/5.0 (Android 15; Tablet; rv:140.0) Gecko/140.0 Firefox/140.0", "Android", "Firefox 140.0", "Tablet")]
    [InlineData("HistoryBrowser/1.0", "Unknown", "Unknown", "Unknown")]
    public void Browser_details_are_inferred_without_using_server_machine_details(
        string agent, string operatingSystem, string browser, string deviceType)
    {
        var service = Service();
        var context = Context();
        context.Request.Headers.UserAgent = agent;
        context.Request.Headers["X-Forwarded-For"] = "8.8.8.8";
        var session = new UserLoginHistory();

        service.ApplyTo(session, context, new LoginDeviceInput { DeviceName = "Reception laptop", TimeZone = "Asia/Kolkata" });

        Assert.Equal(operatingSystem, session.OperatingSystem);
        Assert.Equal(browser, session.Browser);
        Assert.Equal(deviceType, session.DeviceType);
        Assert.Equal("Reception laptop", session.DeviceName);
        Assert.Equal("Asia/Kolkata", session.TimeZone);
        Assert.Equal("203.0.113.10", session.IpAddress);
        Assert.Equal("IpOnly", session.LocationSource);
        Assert.Null(session.Latitude);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.2")]
    [InlineData("172.16.4.5")]
    [InlineData("169.254.10.20")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.20")]
    public void Private_addresses_are_labelled_local_without_inventing_a_city(string address)
    {
        var context = Context();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        var session = new UserLoginHistory();
        Service().ApplyTo(session, context, null);
        Assert.Equal("LocalNetwork", session.LocationSource);
        Assert.Null(session.Latitude);
        Assert.Null(session.Longitude);
    }

    [Fact]
    public void Device_cookie_is_protected_and_reused_but_a_changed_cookie_gets_a_new_identifier()
    {
        var service = Service();
        var first = Context();
        var original = new UserLoginHistory();
        service.ApplyTo(original, first, null);
        var cookie = first.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.DoesNotContain(original.DeviceId!.Value.ToString(), cookie);

        var second = Context();
        second.Request.Headers.Cookie = cookie.Split(';')[0];
        var reused = new UserLoginHistory();
        service.ApplyTo(reused, second, null);
        Assert.Equal(original.DeviceId, reused.DeviceId);

        var changed = Context();
        changed.Request.Headers.Cookie = LoginMetadataService.DeviceCookieName + "=invalid-cookie";
        var replacement = new UserLoginHistory();
        service.ApplyTo(replacement, changed, null);
        Assert.NotEqual(original.DeviceId, replacement.DeviceId);
    }

    [Fact]
    public void Shared_coordinates_keep_accuracy_and_timestamp()
    {
        var session = new UserLoginHistory();
        Service().ApplyTo(session, Context(), SharedLocation());
        Assert.Equal(19.076m, session.Latitude);
        Assert.Equal(72.8777m, session.Longitude);
        Assert.Equal(25.5m, session.LocationAccuracyMeters);
        Assert.Equal(Now.UtcDateTime, session.LocationCapturedAtUtc);
        Assert.Equal("BrowserGeolocation", session.LocationSource);
        Assert.Equal("Granted", session.LocationStatus);
    }

    [Theory]
    [InlineData("Denied")]
    [InlineData("NotShared")]
    [InlineData("Timeout")]
    [InlineData("Unavailable")]
    public void Coordinates_are_ignored_when_the_user_does_not_share_them(string status)
    {
        var input = SharedLocation();
        input.LocationStatus = status;
        var session = new UserLoginHistory();
        Service().ApplyTo(session, Context(), input);
        Assert.Null(session.Latitude);
        Assert.Null(session.Longitude);
        Assert.Null(session.LocationAccuracyMeters);
        Assert.Null(session.LocationCapturedAtUtc);
        Assert.Equal(status, session.LocationStatus);
        Assert.Equal("IpOnly", session.LocationSource);
    }

    [Theory]
    [InlineData("91", "72", "10")]
    [InlineData("19", "181", "10")]
    [InlineData("NaN", "72", "10")]
    [InlineData("19", "Infinity", "10")]
    [InlineData("19", "72", "-1")]
    public void Invalid_coordinates_are_not_stored(string latitude, string longitude, string accuracy)
    {
        var input = SharedLocation();
        input.Latitude = latitude;
        input.Longitude = longitude;
        input.AccuracyMeters = accuracy;
        var session = new UserLoginHistory();
        Service().ApplyTo(session, Context(), input);
        Assert.Equal("Invalid", session.LocationStatus);
        Assert.Null(session.Latitude);
    }

    [Fact]
    public void Stale_coordinates_are_ignored_and_explicit_removal_overrides_pending_setup_location()
    {
        var service = Service();
        var shared = SharedLocation();
        shared.DeviceName = "Office laptop";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(LoginMetadataService.PendingDeviceClaim, service.SerializePendingDevice(shared)!)
        }));

        var carried = service.MergePendingDevice(principal, new LoginDeviceInput());
        Assert.Equal(shared.Latitude, carried.Latitude);
        Assert.Equal("Office laptop", carried.DeviceName);

        var removed = service.MergePendingDevice(principal, new LoginDeviceInput { LocationStatus = "NotShared" });
        Assert.Null(removed.Latitude);
        Assert.Equal("NotShared", removed.LocationStatus);

        shared.CapturedAtUnixMs = Now.AddMinutes(-6).ToUnixTimeMilliseconds().ToString();
        var session = new UserLoginHistory();
        service.ApplyTo(session, Context(), shared);
        Assert.Equal("Stale", session.LocationStatus);
        Assert.Null(session.Latitude);
    }

    private static LoginMetadataService Service()
    {
        return new LoginMetadataService(new EphemeralDataProtectionProvider(), new FixedClock());
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        return context;
    }

    private static LoginDeviceInput SharedLocation()
    {
        return new LoginDeviceInput
        {
            LocationStatus = "Granted",
            Latitude = "19.076",
            Longitude = "72.8777",
            AccuracyMeters = "25.5",
            CapturedAtUnixMs = Now.ToUnixTimeMilliseconds().ToString()
        };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
