using FjordWatch.Domain;

namespace FjordWatch.Api.Contracts;

/// <summary>
/// Minimal GeoJSON shapes (per RFC 7946) used by the track endpoint. We hand-roll
/// rather than depend on `GeoJSON.Net` because the surface we need is tiny and
/// the target frontend is a bespoke Leaflet view.
/// </summary>
public static class GeoJson
{
    public sealed record LineStringFeature(
        string Type,
        Geometry Geometry,
        Properties Properties);

    public sealed record Geometry(string Type, IReadOnlyList<double[]> Coordinates);

    public sealed record Properties(
        long Mmsi,
        DateTimeOffset? Start,
        DateTimeOffset? End,
        int PointCount);

    /// <summary>
    /// Build a `LineString` Feature from a track. A track with fewer than two
    /// points returns a Feature with an empty coordinate list (callers can
    /// inspect <c>PointCount</c>).
    /// </summary>
    public static LineStringFeature ToLineString(Track track)
    {
        var coords = new List<double[]>(track.Points.Count);
        foreach (var p in track.Points)
        {
            coords.Add([p.Longitude, p.Latitude]);
        }

        return new LineStringFeature(
            "Feature",
            new Geometry("LineString", coords),
            new Properties(track.Mmsi, track.Start, track.End, coords.Count));
    }
}
