using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class GeoLocationService : IGeoLocationService
{
    private const int MaximumResponseBytes = 262144;
    private readonly HttpClient _client;
    private readonly GeocodingSettings _settings;
    private readonly ILogger<GeoLocationService> _logger;

    public GeoLocationService(HttpClient client, IOptions<GeocodingSettings> settings, ILogger<GeoLocationService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task ApplyAsync(UserLoginHistory session, CancellationToken cancellationToken = default)
    {
        session.DeviceFormFactor = session.DeviceType;
        session.GeoLocation = null;
        session.LocationResponse = null;
        session.GeocodingProvider = null;
        session.GeocodingStatus = "NotShared";

        if (session.LocationStatus != "Granted" || session.LocationSource != "BrowserGeolocation" ||
            session.Latitude is null || session.Longitude is null ||
            session.Latitude < -90 || session.Latitude > 90 || session.Longitude < -180 || session.Longitude > 180)
        {
            return;
        }

        // Retain a truthful offline estimate if the external lookup cannot complete.
        if (!string.IsNullOrWhiteSpace(session.AreaName))
        {
            session.GeoLocation = "Near " + string.Join(", ", new[] { session.AreaName, session.AreaRegion, session.AreaCountry }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            session.GeocodingProvider = "GeoNames";
            session.LocationResponse = JsonSerializer.Serialize(new
            {
                provider = "GeoNames", approximate = true, area = session.AreaName,
                region = session.AreaRegion, country = session.AreaCountry,
                geoNamesId = session.AreaGeoNamesId, distanceKm = session.AreaDistanceKm
            });
        }

        session.GeocodingStatus = "NotConfigured";
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)) return;
        if (_settings.ApiKey.Any(char.IsControl))
        {
            session.GeocodingStatus = "InvalidConfiguration";
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            string latitude = session.Latitude.Value.ToString("0.######", CultureInfo.InvariantCulture);
            string longitude = session.Longitude.Value.ToString("0.######", CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://geocode.googleapis.com/v4/geocode/location/{latitude},{longitude}?languageCode=en");
            // Keep the API key out of URLs, browser code and ordinary HttpClient logs.
            request.Headers.Add("X-Goog-Api-Key", _settings.ApiKey.Trim());
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                session.GeocodingStatus = "HttpError";
                _logger.LogWarning("Address lookup returned HTTP {StatusCode}", (int)response.StatusCode);
                return;
            }

            await using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await content.ReadAsync(bytes, timeout.Token)) != 0)
            {
                if (buffer.Length + read > MaximumResponseBytes)
                {
                    session.GeocodingStatus = "ResponseTooLarge";
                    return;
                }
                buffer.Write(bytes, 0, read);
            }

            var json = Encoding.UTF8.GetString(buffer.ToArray());
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                session.GeocodingStatus = "InvalidResponse";
                return;
            }
            if (results.GetArrayLength() == 0)
            {
                session.GeocodingStatus = "NoResult";
                return;
            }

            var first = results[0];
            if (first.ValueKind != JsonValueKind.Object ||
                (!first.TryGetProperty("formattedAddress", out var address) && !first.TryGetProperty("formatted_address", out address)) ||
                address.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(address.GetString()) || address.GetString()!.Length > 1000)
            {
                session.GeocodingStatus = "InvalidResponse";
                return;
            }

            session.GeoLocation = address.GetString()!.Trim();
            session.LocationResponse = json;
            session.GeocodingProvider = "Google Maps";
            session.GeocodingStatus = "Resolved";
        }
        catch (OperationCanceledException)
        {
            session.GeocodingStatus = cancellationToken.IsCancellationRequested ? "Cancelled" : "Timeout";
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException)
        {
            session.GeocodingStatus = "Unavailable";
            _logger.LogWarning("Address lookup could not finish: {FailureType}", error.GetType().Name);
        }
    }
}
