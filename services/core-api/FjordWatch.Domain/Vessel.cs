namespace FjordWatch.Domain;

public sealed record Vessel(
    long Mmsi,
    string? Name,
    string? CallSign,
    long? Imo,
    short? ShipType,
    string? Destination,
    DateTimeOffset? Eta,
    float? DraughtMeters,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    Position? LastPosition);
