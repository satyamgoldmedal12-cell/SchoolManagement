using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class LoginMetadataService : ILoginMetadataService
{
    public const string DeviceCookieName = "SchoolManagement.Device";
    public const string PendingDeviceClaim = "school:pending-login-device";

    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;

    public LoginMetadataService(IDataProtectionProvider protection, TimeProvider clock)
    {
        _protector = protection.CreateProtector("SchoolManagement.LoginDevice.v1");
        _clock = clock;
    }

    public void ApplyTo(UserLoginHistory session, HttpContext context, LoginDeviceInput? input)
    {
        var device = Normalize(input);
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true)
        {
            address = address.MapToIPv4();
        }

        var userAgent = Limit(context.Request.Headers.UserAgent.ToString(), 512);
        session.IpAddress = Limit(address?.ToString(), 45);
        session.UserAgent = userAgent;
        session.DeviceId = GetDeviceId(context);
        session.DeviceName = device.DeviceName;
        session.OperatingSystem = DetectOperatingSystem(userAgent);
        session.Browser = DetectBrowser(userAgent);
        session.DeviceType = DetectDeviceType(userAgent);
        session.TimeZone = device.TimeZone;
        session.LocationStatus = device.LocationStatus;
        session.Latitude = null;
        session.Longitude = null;
        session.LocationAccuracyMeters = null;
        session.LocationCapturedAtUtc = null;

        if (address is null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            session.LocationSource = "Unavailable";
        }
        else if (IsLocalAddress(address))
        {
            session.LocationSource = "LocalNetwork";
        }
        else
        {
            session.LocationSource = "IpOnly";
        }

        // Coordinates are optional, client-reported metadata, never an authentication factor.
        if (device.LocationStatus != "Granted")
        {
            return;
        }

        bool validCoordinates = decimal.TryParse(device.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && latitude >= -90 && latitude <= 90;
        validCoordinates &= decimal.TryParse(device.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            && longitude >= -180 && longitude <= 180;
        validCoordinates &= decimal.TryParse(device.AccuracyMeters, NumberStyles.Float, CultureInfo.InvariantCulture, out var accuracy)
            && accuracy >= 0 && accuracy <= 99999999.99m;

        if (!validCoordinates || !long.TryParse(device.CapturedAtUnixMs, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var timestamp))
        {
            session.LocationStatus = "Invalid";
            return;
        }

        DateTimeOffset capturedAt;
        try
        {
            capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            session.LocationStatus = "Invalid";
            return;
        }

        var now = _clock.GetUtcNow();
        if (capturedAt < now.AddMinutes(-5) || capturedAt > now.AddMinutes(2))
        {
            session.LocationStatus = "Stale";
            return;
        }

        session.Latitude = decimal.Round(latitude, 6);
        session.Longitude = decimal.Round(longitude, 6);
        session.LocationAccuracyMeters = decimal.Round(accuracy, 2);
        session.LocationCapturedAtUtc = capturedAt.UtcDateTime;
        session.LocationSource = "BrowserGeolocation";
    }

    public string? SerializePendingDevice(LoginDeviceInput? input)
    {
        if (input is null)
        {
            return null;
        }

        // This small value travels only inside the encrypted, ten-minute setup cookie.
        return JsonSerializer.Serialize(Normalize(input));
    }

    public LoginDeviceInput MergePendingDevice(ClaimsPrincipal? principal, LoginDeviceInput? input)
    {
        var current = Normalize(input);
        var pendingJson = principal?.FindFirstValue(PendingDeviceClaim);
        if (string.IsNullOrEmpty(pendingJson) || pendingJson.Length > 2048)
        {
            return current;
        }

        LoginDeviceInput? pending;
        try
        {
            pending = JsonSerializer.Deserialize<LoginDeviceInput>(pendingJson);
        }
        catch (JsonException)
        {
            return current;
        }

        if (pending is null)
        {
            return current;
        }

        pending = Normalize(pending);
        current.DeviceName ??= pending.DeviceName;
        current.TimeZone ??= pending.TimeZone;
        if (current.LocationStatus == "NotRequested" && current.Latitude is null && current.Longitude is null)
        {
            current.LocationStatus = pending.LocationStatus;
            current.Latitude = pending.Latitude;
            current.Longitude = pending.Longitude;
            current.AccuracyMeters = pending.AccuracyMeters;
            current.CapturedAtUnixMs = pending.CapturedAtUnixMs;
        }

        return current;
    }

    private Guid GetDeviceId(HttpContext context)
    {
        if (context.Items[DeviceCookieName] is Guid cachedId)
        {
            return cachedId;
        }

        var cookie = context.Request.Cookies[DeviceCookieName];
        if (!string.IsNullOrEmpty(cookie))
        {
            try
            {
                if (Guid.TryParse(_protector.Unprotect(cookie), out var existingId))
                {
                    context.Items[DeviceCookieName] = existingId;
                    return existingId;
                }
            }
            catch (CryptographicException)
            {
                // A changed or unreadable identifier starts a new browser-device record.
            }
            catch (FormatException)
            {
                // Invalid cookie encoding is treated the same as a missing identifier.
            }
        }

        var deviceId = Guid.NewGuid();
        context.Response.Cookies.Append(DeviceCookieName, _protector.Protect(deviceId.ToString()), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            Expires = _clock.GetUtcNow().AddYears(1)
        });
        context.Items[DeviceCookieName] = deviceId;
        return deviceId;
    }

    private static LoginDeviceInput Normalize(LoginDeviceInput? input)
    {
        var status = input?.LocationStatus;
        if (status == "Pending") status = "Unavailable";
        if (status is not ("Granted" or "Denied" or "Timeout" or "Unavailable" or "NotShared"))
        {
            status = "NotRequested";
        }

        return new LoginDeviceInput
        {
            DeviceName = Limit(input?.DeviceName, 100),
            TimeZone = Limit(input?.TimeZone, 100),
            Latitude = Limit(input?.Latitude, 32),
            Longitude = Limit(input?.Longitude, 32),
            AccuracyMeters = Limit(input?.AccuracyMeters, 32),
            CapturedAtUnixMs = Limit(input?.CapturedAtUnixMs, 20),
            LocationStatus = status
        };
    }

    private static string DetectOperatingSystem(string? userAgent)
    {
        if (Contains(userAgent, "iPhone") || Contains(userAgent, "iPad") || Contains(userAgent, "iPod")) return "iOS / iPadOS";
        if (Contains(userAgent, "Android")) return "Android";
        if (Contains(userAgent, "Windows")) return "Windows";
        if (Contains(userAgent, "CrOS")) return "ChromeOS";
        if (Contains(userAgent, "Macintosh") || Contains(userAgent, "Mac OS X")) return "macOS";
        if (Contains(userAgent, "Linux") || Contains(userAgent, "X11")) return "Linux";
        return "Unknown";
    }

    private static string DetectDeviceType(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Unknown";
        if (Contains(userAgent, "iPad") || Contains(userAgent, "Tablet") ||
            (Contains(userAgent, "Android") && !Contains(userAgent, "Mobile"))) return "Tablet";
        if (Contains(userAgent, "Mobile") || Contains(userAgent, "iPhone") || Contains(userAgent, "iPod")) return "Mobile";
        if (DetectOperatingSystem(userAgent) != "Unknown") return "Desktop";
        return "Unknown";
    }

    private static string DetectBrowser(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Unknown";
        var browsers = new (string Name, string Pattern)[]
        {
            ("Edge", @"(?:Edg|EdgiOS|EdgA)/([\d.]+)"),
            ("Opera", @"(?:OPR|OPT)/([\d.]+)"),
            ("Firefox", @"(?:Firefox|FxiOS)/([\d.]+)"),
            ("Chrome", @"(?:Chrome|CriOS)/([\d.]+)"),
            ("Safari", @"Version/([\d.]+).*Safari/"),
            ("Internet Explorer", @"(?:MSIE |rv:)([\d.]+)")
        };

        foreach (var browser in browsers)
        {
            if (browser.Name == "Internet Explorer" && !Contains(userAgent, "MSIE") && !Contains(userAgent, "Trident")) continue;
            var match = Regex.Match(userAgent, browser.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
            if (match.Success)
            {
                return browser.Name + " " + Limit(match.Groups[1].Value, 32);
            }
        }

        return "Unknown";
    }

    private static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return (bytes[0] & 0xfe) == 0xfc;
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool Contains(string? value, string part)
    {
        return value?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > maximumLength) return value.Substring(0, maximumLength);
        return value;
    }
}
