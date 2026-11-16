using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class LoginDeviceInput
{
    [StringLength(100)]
    [Display(Name = "System name (optional)")]
    public string? DeviceName { get; set; }

    [StringLength(100)]
    public string? TimeZone { get; set; }

    [StringLength(32)]
    public string? Latitude { get; set; }

    [StringLength(32)]
    public string? Longitude { get; set; }

    [StringLength(32)]
    public string? AccuracyMeters { get; set; }

    [StringLength(20)]
    public string? CapturedAtUnixMs { get; set; }

    [StringLength(32)]
    public string LocationStatus { get; set; } = "NotRequested";
}
