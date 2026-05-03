namespace FjordWatch.Domain;

public sealed record SarDetection(
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
    DateTimeOffset CreatedAt);

public interface ISarDetectionRepository
{
    Task<IReadOnlyList<SarDetection>> ListAsync(
        BoundingBox bbox,
        DateTimeOffset since,
        bool onlyDark,
        int limit,
        CancellationToken ct);
}
