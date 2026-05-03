using Dapper;
using FjordWatch.Domain;
using Npgsql;

namespace FjordWatch.Infrastructure;

public sealed class PostgresSarDetectionRepository : ISarDetectionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSarDetectionRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<SarDetection>> ListAsync(
        BoundingBox bbox,
        DateTimeOffset since,
        bool onlyDark,
        int limit,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT
                id,
                scene_id            AS sceneid,
                detected_at         AS detectedat,
                ST_X(geom::geometry) AS longitude,
                ST_Y(geom::geometry) AS latitude,
                confidence,
                is_dark             AS isdark,
                matched_mmsi        AS matchedmmsi,
                match_distance_m    AS matchdistancemeters,
                match_lag_s         AS matchlagseconds,
                created_at          AS createdat
            FROM sar_detections
            WHERE detected_at >= @since
              AND geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)::geography
              AND (@onlyDark = false OR is_dark = true)
            ORDER BY detected_at DESC
            LIMIT @limit";

        var rows = await conn.QueryAsync<SarRow>(new CommandDefinition(
            sql,
            new
            {
                west = bbox.West,
                south = bbox.South,
                east = bbox.East,
                north = bbox.North,
                since = since.UtcDateTime,
                onlyDark,
                limit,
            },
            cancellationToken: ct));

        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class SarRow
    {
        public long Id { get; init; }
        public string SceneId { get; init; } = "";
        public DateTime DetectedAt { get; init; }
        public double Longitude { get; init; }
        public double Latitude { get; init; }
        public float Confidence { get; init; }
        public bool IsDark { get; init; }
        public long? MatchedMmsi { get; init; }
        public float? MatchDistanceMeters { get; init; }
        public float? MatchLagSeconds { get; init; }
        public DateTime CreatedAt { get; init; }

        public SarDetection ToDomain() => new(
            Id,
            SceneId,
            new DateTimeOffset(DetectedAt, TimeSpan.Zero),
            Longitude,
            Latitude,
            Confidence,
            IsDark,
            MatchedMmsi,
            MatchDistanceMeters,
            MatchLagSeconds,
            new DateTimeOffset(CreatedAt, TimeSpan.Zero));
    }
}
