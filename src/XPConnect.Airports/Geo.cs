namespace XPConnect.Airports;

public static class Geo
{
    private const double EarthRadiusMeters = 6_371_008.8;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = Radians(lat1);
        var p2 = Radians(lat2);
        var dp = Radians(lat2 - lat1);
        var dl = Radians(lon2 - lon1);
        var a = (Math.Sin(dp / 2) * Math.Sin(dp / 2)) + (Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2));
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Initial true bearing from point 1 to point 2, 0-360 degrees.</summary>
    public static double BearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = Radians(lat1);
        var p2 = Radians(lat2);
        var dl = Radians(lon2 - lon1);
        var y = Math.Sin(dl) * Math.Cos(p2);
        var x = (Math.Cos(p1) * Math.Sin(p2)) - (Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl));
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

/// <summary>
/// Flat east/north metres around a reference point, the frame MSFS uses for taxi point and parking BIAS_X (east)
/// and BIAS_Z (north). Good to well under a metre across an airport.
/// </summary>
public readonly struct LocalFrame(double latitude, double longitude)
{
    private const double MetersPerDegree = 6_371_008.8 * Math.PI / 180;

    public double Latitude { get; } = latitude;

    public double Longitude { get; } = longitude;

    private double Cos => Math.Cos(Latitude * Math.PI / 180);

    public (double X, double Z) ToLocal(double latitude, double longitude) =>
        ((longitude - Longitude) * MetersPerDegree * Cos, (latitude - Latitude) * MetersPerDegree);

    public (double Latitude, double Longitude) FromLocal(double x, double z) =>
        (Latitude + (z / MetersPerDegree), Longitude + (x / (MetersPerDegree * Cos)));
}
