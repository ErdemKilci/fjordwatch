namespace FjordWatch.Web.Models;

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
    DateTimeOffset CreatedAt);
