namespace FjordWatch.Domain;

public sealed record Track(long Mmsi, IReadOnlyList<TrackPoint> Points)
{
    public DateTimeOffset? Start => Points.Count == 0 ? null : Points[0].Timestamp;
    public DateTimeOffset? End => Points.Count == 0 ? null : Points[^1].Timestamp;
}

public sealed record TrackPoint(
    DateTimeOffset Timestamp,
    double Longitude,
    double Latitude,
    float? SpeedOverGroundKnots,
    float? CourseOverGroundDegrees);
