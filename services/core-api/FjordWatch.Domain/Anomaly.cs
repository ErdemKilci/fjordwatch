namespace FjordWatch.Domain;

public sealed record Anomaly(
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
    DateTimeOffset CreatedAt);

public interface IAnomalyRepository
{
    Task<IReadOnlyList<Anomaly>> ListAsync(
        DateTimeOffset since,
        float minScore,
        int limit,
        CancellationToken ct);
}
