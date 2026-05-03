using System.Globalization;
using FjordWatch.Domain;

namespace FjordWatch.Agent.Tools;

public sealed class NearestVesselsTool : IAgentTool
{
    private readonly IVesselRepository _repo;

    public NearestVesselsTool(IVesselRepository repo)
    {
        _repo = repo;
    }

    public string Name => "nearest_vessels";
    public string Description => "Vessels currently inside a small bounding box around a lat/lon point. Use when the user asks about vessels near a specific place.";
    public string ParameterSchema => "{ lat: number, lon: number, radius_km: number (default 25, max 200) }";

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!TryParseDouble(args, "lat", out var lat) || !TryParseDouble(args, "lon", out var lon))
        {
            return Failure("missing or invalid lat/lon");
        }
        if (!TryParseDouble(args, "radius_km", out var radiusKm))
        {
            radiusKm = 25.0;
        }
        radiusKm = Math.Clamp(radiusKm, 1.0, 200.0);

        var bbox = BboxFromCenter(lat, lon, radiusKm);
        var vessels = await _repo.GetVesselsInBboxAsync(bbox, categories: null, limit: 50, ct);

        var summary = vessels.Count == 0
            ? $"No vessels in the last 6 hours within {radiusKm:F0} km of ({lat:F3}, {lon:F3})."
            : $"{vessels.Count} vessel(s) within {radiusKm:F0} km of ({lat:F3}, {lon:F3}). " +
              "Examples: " + string.Join(", ", vessels.Take(5).Select(v => v.Name ?? v.Mmsi.ToString(CultureInfo.InvariantCulture)));

        return new ToolResult(
            summary,
            new Citation(
                Source: Name,
                Description: $"{vessels.Count} vessels within {radiusKm:F0} km",
                Parameters: new Dictionary<string, string>
                {
                    ["lat"] = lat.ToString("F4", CultureInfo.InvariantCulture),
                    ["lon"] = lon.ToString("F4", CultureInfo.InvariantCulture),
                    ["radius_km"] = radiusKm.ToString("F0", CultureInfo.InvariantCulture),
                }));
    }

    private static BoundingBox BboxFromCenter(double lat, double lon, double radiusKm)
    {
        // 1 degree of latitude ≈ 111 km. Longitude shrinks with cos(lat).
        var latDelta = radiusKm / 111.0;
        var lonDelta = radiusKm / (111.0 * Math.Max(0.05, Math.Cos(lat * Math.PI / 180.0)));
        return new BoundingBox(
            West: Math.Max(-180.0, lon - lonDelta),
            South: Math.Max(-90.0, lat - latDelta),
            East: Math.Min(180.0, lon + lonDelta),
            North: Math.Min(90.0, lat + latDelta));
    }

    private static bool TryParseDouble(IReadOnlyDictionary<string, string> args, string key, out double value)
    {
        value = 0.0;
        return args.TryGetValue(key, out var raw) &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static ToolResult Failure(string reason) =>
        new(reason, new Citation(Source: "nearest_vessels", Description: $"error: {reason}", Parameters: new Dictionary<string, string>()));
}
