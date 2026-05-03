using FjordWatch.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FjordWatch.Api.Endpoints;

public static class SarEndpoints
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;
    public static readonly TimeSpan DefaultSinceWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxSinceWindow = TimeSpan.FromDays(30);

    public static void MapSarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sar").WithTags("sar");
        group.MapGet("/", ListDetections);
    }

    public static async Task<Results<Ok<IReadOnlyList<SarDetectionDto>>, BadRequest<string>>> ListDetections(
        ISarDetectionRepository repo,
        string bbox,
        DateTimeOffset? since,
        bool? onlyDark,
        int? limit,
        CancellationToken ct)
    {
        if (!BoundingBox.TryParse(bbox, out var box))
        {
            return TypedResults.BadRequest("invalid bbox; expected 'west,south,east,north'");
        }

        var sinceUtc = since ?? DateTimeOffset.UtcNow - DefaultSinceWindow;
        if (DateTimeOffset.UtcNow - sinceUtc > MaxSinceWindow)
        {
            return TypedResults.BadRequest($"'since' is older than {MaxSinceWindow.TotalDays:F0} days");
        }

        var clampedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await repo.ListAsync(box, sinceUtc, onlyDark ?? false, clampedLimit, ct);
        return TypedResults.Ok((IReadOnlyList<SarDetectionDto>)rows.Select(SarDetectionDto.FromDomain).ToList());
    }
}

public sealed record SarDetectionDto(
    long Id,
    string SceneId,
    DateTimeOffset DetectedAt,
    double Longitude,
    double Latitude,
    float Confidence,
    bool IsDark,
    long? MatchedMmsi,
    float? MatchDistanceMeters,
    float? MatchLagSeconds,
    DateTimeOffset CreatedAt)
{
    public static SarDetectionDto FromDomain(SarDetection d) => new(
        d.Id,
        d.SceneId,
        d.DetectedAt,
        d.Longitude,
        d.Latitude,
        d.Confidence,
        d.IsDark,
        d.MatchedMmsi,
        d.MatchDistanceMeters,
        d.MatchLagSeconds,
        d.CreatedAt);
}
