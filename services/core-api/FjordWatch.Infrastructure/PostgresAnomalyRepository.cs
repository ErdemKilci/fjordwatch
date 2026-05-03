using Dapper;
using FjordWatch.Domain;
using Npgsql;

namespace FjordWatch.Infrastructure;

public sealed class PostgresAnomalyRepository : IAnomalyRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAnomalyRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<Anomaly>> ListAsync(
        DateTimeOffset since,
        float minScore,
        int limit,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT
                a.id            AS id,
                a.mmsi          AS mmsi,
                v.name          AS vesselname,
                a.window_start  AS windowstart,
                a.window_end    AS windowend,
                a.score         AS score,
                a.iso_score     AS isoscore,
                a.lstm_score    AS lstmscore,
                a.contributing::text AS contributing,
                a.model_versions::text AS modelversions,
                a.created_at    AS createdat
            FROM vessel_anomalies a
            LEFT JOIN vessels v ON v.mmsi = a.mmsi
            WHERE a.created_at >= @since
              AND a.score >= @minScore
            ORDER BY a.created_at DESC, a.score DESC
            LIMIT @limit";

        var rows = await conn.QueryAsync<AnomalyRow>(new CommandDefinition(
            sql,
            new
            {
                since = since.UtcDateTime,
                minScore,
                limit,
            },
            cancellationToken: ct));

        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class AnomalyRow
    {
        public long Id { get; init; }
        public long Mmsi { get; init; }
        public string? VesselName { get; init; }
        public DateTime WindowStart { get; init; }
        public DateTime WindowEnd { get; init; }
        public float Score { get; init; }
        public float? IsoScore { get; init; }
        public float? LstmScore { get; init; }
        public string? Contributing { get; init; }
        public string? ModelVersions { get; init; }
        public DateTime CreatedAt { get; init; }

        public Anomaly ToDomain() => new(
            Id,
            Mmsi,
            VesselName,
            new DateTimeOffset(WindowStart, TimeSpan.Zero),
            new DateTimeOffset(WindowEnd, TimeSpan.Zero),
            Score,
            IsoScore,
            LstmScore,
            Contributing,
            ModelVersions,
            new DateTimeOffset(CreatedAt, TimeSpan.Zero));
    }
}
