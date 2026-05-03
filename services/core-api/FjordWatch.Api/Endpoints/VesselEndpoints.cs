using FjordWatch.Api.Contracts;
using FjordWatch.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FjordWatch.Api.Endpoints;

public static class VesselEndpoints
{
    public const int DefaultBboxLimit = 2000;
    public const int MaxBboxLimit = 5000;
    public static readonly TimeSpan MaxTrackWindow = TimeSpan.FromHours(48);

    public static void MapVesselEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vessels").WithTags("vessels");

        group.MapGet("/", GetVesselsInBbox);
        group.MapGet("/{mmsi:long}", GetVessel);
        group.MapGet("/{mmsi:long}/track", GetVesselTrack);
    }

    public static async Task<Results<Ok<IReadOnlyList<VesselDto>>, BadRequest<string>>> GetVesselsInBbox(
        IVesselRepository repo,
        string bbox,
        string? types,
        int? limit,
        CancellationToken ct)
    {
        if (!BoundingBox.TryParse(bbox, out var box))
        {
            return TypedResults.BadRequest("invalid bbox; expected 'west,south,east,north'");
        }

        var clampedLimit = Math.Clamp(limit ?? DefaultBboxLimit, 1, MaxBboxLimit);

        var categories = ParseCategories(types);
        if (categories is null)
        {
            return TypedResults.BadRequest("invalid types; expected comma-separated ShipTypeCategory names");
        }

        var vessels = await repo.GetVesselsInBboxAsync(box, categories, clampedLimit, ct);
        return TypedResults.Ok((IReadOnlyList<VesselDto>)vessels.Select(VesselDto.FromDomain).ToList());
    }

    public static async Task<Results<Ok<VesselDto>, NotFound>> GetVessel(
        IVesselRepository repo,
        long mmsi,
        CancellationToken ct)
    {
        var vessel = await repo.GetVesselAsync(mmsi, ct);
        return vessel is null ? TypedResults.NotFound() : TypedResults.Ok(VesselDto.FromDomain(vessel));
    }

    public static async Task<Results<Ok<GeoJson.LineStringFeature>, BadRequest<string>>> GetVesselTrack(
        IVesselRepository repo,
        long mmsi,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var toUtc = to ?? DateTimeOffset.UtcNow;
        var fromUtc = from ?? toUtc - TimeSpan.FromHours(24);

        if (fromUtc >= toUtc)
        {
            return TypedResults.BadRequest("'from' must be earlier than 'to'");
        }

        if (toUtc - fromUtc > MaxTrackWindow)
        {
            return TypedResults.BadRequest($"track window exceeds {MaxTrackWindow.TotalHours:F0} hours");
        }

        var track = await repo.GetTrackAsync(mmsi, fromUtc, toUtc, ct);
        return TypedResults.Ok(GeoJson.ToLineString(track));
    }

    /// <summary>
    /// Parse a comma-separated list of <see cref="ShipTypeCategory"/> names.
    /// Returns null on any invalid entry, empty list when input is empty.
    /// </summary>
    public static IReadOnlyCollection<ShipTypeCategory>? ParseCategories(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<ShipTypeCategory>(parts.Length);
        foreach (var part in parts)
        {
            if (!Enum.TryParse<ShipTypeCategory>(part, ignoreCase: true, out var category))
            {
                return null;
            }
            result.Add(category);
        }
        return result;
    }
}
