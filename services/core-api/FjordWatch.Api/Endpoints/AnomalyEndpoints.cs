using FjordWatch.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FjordWatch.Api.Endpoints;

public static class AnomalyEndpoints
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public static readonly TimeSpan DefaultSinceWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxSinceWindow = TimeSpan.FromDays(30);

    public static void MapAnomalyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/anomalies").WithTags("anomalies");
        group.MapGet("/", ListAnomalies);
    }

    public static async Task<Results<Ok<IReadOnlyList<AnomalyDto>>, BadRequest<string>>> ListAnomalies(
        IAnomalyRepository repo,
        DateTimeOffset? since,
        float? minScore,
        int? limit,
        CancellationToken ct)
    {
        var sinceUtc = since ?? DateTimeOffset.UtcNow - DefaultSinceWindow;
        if (DateTimeOffset.UtcNow - sinceUtc > MaxSinceWindow)
        {
            return TypedResults.BadRequest($"'since' is older than {MaxSinceWindow.TotalDays:F0} days");
        }

        var minScoreClamped = Math.Clamp(minScore ?? 0.0f, 0.0f, 1.0f);
        var limitClamped = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var rows = await repo.ListAsync(sinceUtc, minScoreClamped, limitClamped, ct);
        return TypedResults.Ok((IReadOnlyList<AnomalyDto>)rows.Select(AnomalyDto.FromDomain).ToList());
    }
}

public sealed record AnomalyDto(
    long Id,
    long Mmsi,
    string? VesselName,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    float Score,
    float? IsoScore,
    float? LstmScore,
    string? Contributing,
    string? ModelVersions,
    DateTimeOffset CreatedAt)
{
    public static AnomalyDto FromDomain(Anomaly a) => new(
        a.Id,
        a.Mmsi,
        a.VesselName,
        a.WindowStart,
        a.WindowEnd,
        a.Score,
        a.IsoScore,
        a.LstmScore,
        a.Contributing,
        a.ModelVersions,
        a.CreatedAt);
}
