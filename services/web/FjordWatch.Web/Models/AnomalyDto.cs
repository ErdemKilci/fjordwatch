namespace FjordWatch.Web.Models;

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
    DateTimeOffset CreatedAt);
