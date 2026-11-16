using System.Globalization;
using System.IO.Compression;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class LoginAreaService : ILoginAreaService
{
    private const double MaximumDistanceKm = 25;
    private readonly Lazy<Dictionary<int, List<Place>>> _places;

    public LoginAreaService(IWebHostEnvironment environment, ILogger<LoginAreaService> logger)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "Locations", "places.tsv.gz");
        _places = new Lazy<Dictionary<int, List<Place>>>(() => LoadPlaces(path, logger));
    }

    public void ApplyTo(UserLoginHistory session)
    {
        session.AreaName = null;
        session.AreaRegion = null;
        session.AreaCountry = null;
        session.AreaGeoNamesId = null;
        session.AreaDistanceKm = null;
        session.AreaSource = "NotShared";

        if (session.LocationStatus != "Granted" || session.LocationSource != "BrowserGeolocation" ||
            session.Latitude is null || session.Longitude is null)
        {
            return;
        }

        var latitude = (double)session.Latitude.Value;
        var longitude = (double)session.Longitude.Value;
        if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
        {
            session.AreaSource = "Unavailable";
            return;
        }

        var places = _places.Value;
        if (places.Count == 0)
        {
            session.AreaSource = "Unavailable";
            return;
        }

        Place? nearest = null;
        double distanceKm = MaximumDistanceKm;
        int latitudeCell = (int)Math.Floor(latitude);
        // A 25 km radius fits within the current latitude degree and its two neighbours.
        for (int cell = latitudeCell - 1; cell <= latitudeCell + 1; cell++)
        {
            if (!places.TryGetValue(cell, out var candidates)) continue;
            foreach (var place in candidates)
            {
                double distance = DistanceKm(latitude, longitude, place.Latitude, place.Longitude);
                if (distance <= distanceKm)
                {
                    nearest = place;
                    distanceKm = distance;
                }
            }
        }

        if (nearest is null)
        {
            session.AreaSource = "NoNearbyPlace";
            return;
        }

        session.AreaName = nearest.Name;
        session.AreaRegion = nearest.Region;
        session.AreaCountry = nearest.Country;
        session.AreaGeoNamesId = nearest.Id;
        session.AreaDistanceKm = decimal.Round((decimal)distanceKm, 3);
        session.AreaSource = "GeoNamesNearestPlace";
    }

    private static Dictionary<int, List<Place>> LoadPlaces(string path, ILogger logger)
    {
        var result = new Dictionary<int, List<Place>>();
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            while (reader.ReadLine() is { } line)
            {
                var values = line.Split('\t');
                if (values.Length != 6 || !int.TryParse(values[0], out var id) ||
                    !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                    !double.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                    !double.IsFinite(latitude) || !double.IsFinite(longitude) ||
                    latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) continue;

                var place = new Place(id, Limit(values[1], 200), latitude, longitude,
                    Limit(values[4], 100), Limit(values[5], 100));
                int cell = (int)Math.Floor(latitude);
                if (!result.TryGetValue(cell, out var bucket))
                {
                    bucket = new List<Place>();
                    result[cell] = bucket;
                }
                bucket.Add(place);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogWarning("Offline login-area data could not be loaded: {FailureType}", error.GetType().Name);
            return new Dictionary<int, List<Place>>();
        }
        return result;
    }

    private static double DistanceKm(double latitude, double longitude, double otherLatitude, double otherLongitude)
    {
        const double radians = Math.PI / 180;
        double latDifference = (otherLatitude - latitude) * radians;
        double lonDifference = (otherLongitude - longitude) * radians;
        double value = Math.Pow(Math.Sin(latDifference / 2), 2) +
            Math.Cos(latitude * radians) * Math.Cos(otherLatitude * radians) * Math.Pow(Math.Sin(lonDifference / 2), 2);
        return 6371.0088 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(value, 0, 1)));
    }

    private static string Limit(string value, int length)
    {
        value = value.Trim();
        return value.Length <= length ? value : value[..length];
    }

    private sealed record Place(int Id, string Name, double Latitude, double Longitude, string Region, string Country);
}
