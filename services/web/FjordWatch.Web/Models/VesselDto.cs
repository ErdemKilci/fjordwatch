namespace FjordWatch.Web.Models;

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
    PositionDto? LastPosition);

public sealed record PositionDto(
    long Mmsi,
    DateTimeOffset Timestamp,
    double Longitude,
    double Latitude,
    float? SpeedOverGroundKnots,
    float? CourseOverGroundDegrees,
    short? HeadingDegrees,
    short? NavigationStatus,
    short MessageType);

public sealed record GeoJsonLineString(
    string Type,
    GeoJsonGeometry Geometry,
    GeoJsonProperties Properties);

public sealed record GeoJsonGeometry(string Type, IReadOnlyList<double[]> Coordinates);

public sealed record GeoJsonProperties(
    long Mmsi,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    int PointCount);

public sealed record PositionUpdate(
    long Mmsi,
    DateTimeOffset Ts,
    double Latitude,
    double Longitude,
    float? Sog,
    float? Cog,
    ushort? Heading);
