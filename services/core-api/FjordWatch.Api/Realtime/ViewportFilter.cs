using FjordWatch.Domain;

namespace FjordWatch.Api.Realtime;

/// <summary>
/// Per-connection rule set used by the SignalR hub to decide whether to send
/// a position update to a specific client. Combines a bbox filter and a
/// per-MMSI rate limit so a viewport showing thousands of vessels does not
/// drown the client at full feed rate.
/// </summary>
public sealed class ViewportFilter
{
    public BoundingBox? Viewport { get; set; }
    public TimeSpan MinIntervalPerVessel { get; set; } = TimeSpan.FromSeconds(3);

    private readonly Dictionary<long, DateTimeOffset> _lastSent = [];

    public bool ShouldSend(long mmsi, double longitude, double latitude, DateTimeOffset now)
    {
        if (Viewport is { } bbox && !bbox.Contains(longitude, latitude))
        {
            return false;
        }

        if (_lastSent.TryGetValue(mmsi, out var last) && now - last < MinIntervalPerVessel)
        {
            return false;
        }

        _lastSent[mmsi] = now;
        return true;
    }

    public void Reset()
    {
        _lastSent.Clear();
    }
}
