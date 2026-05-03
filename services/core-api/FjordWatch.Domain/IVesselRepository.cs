namespace FjordWatch.Domain;

public interface IVesselRepository
{
    /// <summary>
    /// Vessels whose latest position lies inside the given bounding box,
    /// optionally filtered to one or more ship type categories.
    /// </summary>
    Task<IReadOnlyList<Vessel>> GetVesselsInBboxAsync(
        BoundingBox bbox,
        IReadOnlyCollection<ShipTypeCategory>? categories,
        int limit,
        CancellationToken ct);

    Task<Vessel?> GetVesselAsync(long mmsi, CancellationToken ct);

    /// <summary>
    /// All position fixes for a vessel between <paramref name="fromUtc"/> and
    /// <paramref name="toUtc"/>, ordered by timestamp ascending.
    /// </summary>
    Task<Track> GetTrackAsync(long mmsi, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}
