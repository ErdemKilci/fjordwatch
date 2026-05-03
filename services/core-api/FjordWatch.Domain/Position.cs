namespace FjordWatch.Domain;

public sealed record Position(
    long Mmsi,
    DateTimeOffset Timestamp,
    double Longitude,
    double Latitude,
    float? SpeedOverGroundKnots,
    float? CourseOverGroundDegrees,
    short? HeadingDegrees,
    float? RateOfTurnDegPerMin,
    short? NavigationStatus,
    short MessageType);
