namespace SchoolManagement.Models;

public class UserLoginHistory
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public DateTime LoginAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    // Only an explicit logout fills LogoutAtUtc.
    public DateTime? LogoutAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string? EndReason { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Browser { get; set; }
    public string? DeviceType { get; set; }
    public string? TimeZone { get; set; }

    public string? LocationSource { get; set; }
    public string? LocationStatus { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? LocationAccuracyMeters { get; set; }
    public DateTime? LocationCapturedAtUtc { get; set; }

    public string? AreaName { get; set; }
    public string? AreaRegion { get; set; }
    public string? AreaCountry { get; set; }
    public string? AreaSource { get; set; }
    public int? AreaGeoNamesId { get; set; }
    public decimal? AreaDistanceKm { get; set; }

    public string? GeoLocation { get; set; }
    public string? DeviceFormFactor { get; set; }
    public string? LocationResponse { get; set; }
    public string? GeocodingProvider { get; set; }
    public string? GeocodingStatus { get; set; }
}
