using System.Data;
using Dapper;
using FjordWatch.Domain;
using Npgsql;

namespace FjordWatch.Infrastructure;

public sealed class PostgresVesselRepository : IVesselRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresVesselRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<Vessel>> GetVesselsInBboxAsync(
        BoundingBox bbox,
        IReadOnlyCollection<ShipTypeCategory>? categories,
        int limit,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var shipTypeFilter = ShipTypeFilter.FromCategories(categories);

        const string sql = @"
            WITH latest AS (
                SELECT DISTINCT ON (p.mmsi)
                    p.mmsi, p.ts, p.geom, p.sog_knots, p.cog_deg, p.heading_deg,
                    p.rot_deg_per_min, p.nav_status, p.msg_type
                FROM positions p
                WHERE p.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)::geography
                  AND p.ts > now() - INTERVAL '6 hours'
                ORDER BY p.mmsi, p.ts DESC
            )
            SELECT
                v.mmsi, v.name, v.call_sign AS callsign, v.imo, v.ship_type AS shiptype,
                v.destination, v.eta, v.draught_m AS draughtmeters,
                v.first_seen AS firstseen, v.last_seen AS lastseen,
                latest.ts                AS pos_ts,
                ST_X(latest.geom::geometry) AS pos_lon,
                ST_Y(latest.geom::geometry) AS pos_lat,
                latest.sog_knots         AS pos_sog,
                latest.cog_deg           AS pos_cog,
                latest.heading_deg       AS pos_heading,
                latest.rot_deg_per_min   AS pos_rot,
                latest.nav_status        AS pos_navstatus,
                latest.msg_type          AS pos_msgtype
            FROM latest
            JOIN vessels v ON v.mmsi = latest.mmsi
            WHERE (@hasShipTypes = false OR v.ship_type = ANY(@shipTypes))
            ORDER BY latest.ts DESC
            LIMIT @limit";

        var rows = await conn.QueryAsync<VesselRow>(new CommandDefinition(
            sql,
            new
            {
                west = bbox.West,
                south = bbox.South,
                east = bbox.East,
                north = bbox.North,
                hasShipTypes = shipTypeFilter.Length > 0,
                shipTypes = shipTypeFilter,
                limit,
            },
            cancellationToken: ct));

        return rows.Select(r => r.ToVessel()).ToList();
    }

    public async Task<Vessel?> GetVesselAsync(long mmsi, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT
                v.mmsi, v.name, v.call_sign AS callsign, v.imo, v.ship_type AS shiptype,
                v.destination, v.eta, v.draught_m AS draughtmeters,
                v.first_seen AS firstseen, v.last_seen AS lastseen,
                p.ts                AS pos_ts,
                ST_X(p.geom::geometry) AS pos_lon,
                ST_Y(p.geom::geometry) AS pos_lat,
                p.sog_knots         AS pos_sog,
                p.cog_deg           AS pos_cog,
                p.heading_deg       AS pos_heading,
                p.rot_deg_per_min   AS pos_rot,
                p.nav_status        AS pos_navstatus,
                p.msg_type          AS pos_msgtype
            FROM vessels v
            LEFT JOIN LATERAL (
                SELECT ts, geom, sog_knots, cog_deg, heading_deg, rot_deg_per_min, nav_status, msg_type
                FROM positions
                WHERE mmsi = v.mmsi
                ORDER BY ts DESC
                LIMIT 1
            ) p ON true
            WHERE v.mmsi = @mmsi";

        var row = await conn.QuerySingleOrDefaultAsync<VesselRow>(new CommandDefinition(
            sql,
            new { mmsi },
            cancellationToken: ct));
        return row?.ToVessel();
    }

    public async Task<Track> GetTrackAsync(long mmsi, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT
                ts                       AS timestamp,
                ST_X(geom::geometry)     AS longitude,
                ST_Y(geom::geometry)     AS latitude,
                sog_knots                AS speedoverGroundknots,
                cog_deg                  AS courseoverGrounddegrees
            FROM positions
            WHERE mmsi = @mmsi
              AND ts >= @fromUtc
              AND ts <= @toUtc
            ORDER BY ts ASC";

        var points = await conn.QueryAsync<TrackPoint>(new CommandDefinition(
            sql,
            new { mmsi, fromUtc = fromUtc.UtcDateTime, toUtc = toUtc.UtcDateTime },
            cancellationToken: ct));

        return new Track(mmsi, points.ToList());
    }

    private sealed class VesselRow
    {
        public long Mmsi { get; init; }
        public string? Name { get; init; }
        public string? CallSign { get; init; }
        public long? Imo { get; init; }
        public short? ShipType { get; init; }
        public string? Destination { get; init; }
        public DateTime? Eta { get; init; }
        public float? DraughtMeters { get; init; }
        public DateTime FirstSeen { get; init; }
        public DateTime LastSeen { get; init; }
        public DateTime? Pos_Ts { get; init; }
        public double? Pos_Lon { get; init; }
        public double? Pos_Lat { get; init; }
        public float? Pos_Sog { get; init; }
        public float? Pos_Cog { get; init; }
        public short? Pos_Heading { get; init; }
        public float? Pos_Rot { get; init; }
        public short? Pos_NavStatus { get; init; }
        public short? Pos_MsgType { get; init; }

        public Vessel ToVessel()
        {
            Position? position = null;
            if (Pos_Ts is { } ts && Pos_Lon is { } lon && Pos_Lat is { } lat && Pos_MsgType is { } msgType)
            {
                position = new Position(
                    Mmsi,
                    new DateTimeOffset(ts, TimeSpan.Zero),
                    lon,
                    lat,
                    Pos_Sog,
                    Pos_Cog,
                    Pos_Heading,
                    Pos_Rot,
                    Pos_NavStatus,
                    msgType);
            }

            return new Vessel(
                Mmsi,
                Name,
                CallSign,
                Imo,
                ShipType,
                Destination,
                Eta is { } eta ? new DateTimeOffset(eta, TimeSpan.Zero) : null,
                DraughtMeters,
                new DateTimeOffset(FirstSeen, TimeSpan.Zero),
                new DateTimeOffset(LastSeen, TimeSpan.Zero),
                position);
        }
    }
}
