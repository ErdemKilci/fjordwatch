using FjordWatch.Domain;

namespace FjordWatch.Api.Contracts;

public sealed record VesselDto(
    long Mmsi,
    string? Name,
    string? CallSign,
    long? Imo,
    short? ShipType,
    string Category,
    string? Destination,
    DateTimeOffset? Eta,
    float? DraughtMeters,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    PositionDto? LastPosition)
{
    public static VesselDto FromDomain(Vessel v) => new(
        v.Mmsi,
        v.Name,
        v.CallSign,
        v.Imo,
        v.ShipType,
        ShipTypeClassifier.Categorize(v.ShipType).ToString(),
        v.Destination,
        v.Eta,
        v.DraughtMeters,
        v.FirstSeen,
        v.LastSeen,
        v.LastPosition is null ? null : PositionDto.FromDomain(v.LastPosition));
}

public sealed record PositionDto(
    long Mmsi,
    DateTimeOffset Timestamp,
    double Longitude,
    double Latitude,
    float? SpeedOverGroundKnots,
    float? CourseOverGroundDegrees,
    short? HeadingDegrees,
    short? NavigationStatus,
    short MessageType)
{
    public static PositionDto FromDomain(Position p) => new(
        p.Mmsi,
        p.Timestamp,
        p.Longitude,
        p.Latitude,
        p.SpeedOverGroundKnots,
        p.CourseOverGroundDegrees,
        p.HeadingDegrees,
        p.NavigationStatus,
        p.MessageType);
}
