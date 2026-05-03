using System.Collections.Concurrent;
using FjordWatch.Domain;
using Microsoft.AspNetCore.SignalR;

namespace FjordWatch.Api.Realtime;

/// <summary>
/// Real-time vessel position hub at <c>/hubs/vessels</c>. Clients call
/// <see cref="SetViewport"/> on connect (and again whenever the map pans or
/// zooms); the <see cref="RedisStreamRelay"/> reads from the connection
/// registry to decide who gets each fanout.
/// </summary>
public sealed class VesselsHub : Hub
{
    private readonly ConnectionRegistry _registry;

    public VesselsHub(ConnectionRegistry registry)
    {
        _registry = registry;
    }

    public override Task OnConnectedAsync()
    {
        _registry.Register(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task SetViewport(double west, double south, double east, double north)
    {
        var bbox = new BoundingBox(west, south, east, north);
        if (!bbox.IsValid)
        {
            throw new HubException("invalid bounding box");
        }
        _registry.UpdateViewport(Context.ConnectionId, bbox);
        return Task.CompletedTask;
    }

    public Task ClearViewport()
    {
        _registry.UpdateViewport(Context.ConnectionId, null);
        return Task.CompletedTask;
    }
}

public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ViewportFilter> _connections = new();

    public void Register(string connectionId) =>
        _connections[connectionId] = new ViewportFilter();

    public void Remove(string connectionId) =>
        _connections.TryRemove(connectionId, out _);

    public void UpdateViewport(string connectionId, BoundingBox? bbox)
    {
        if (_connections.TryGetValue(connectionId, out var filter))
        {
            filter.Viewport = bbox;
            filter.Reset();
        }
    }

    public IReadOnlyDictionary<string, ViewportFilter> Snapshot() => _connections;

    public int Count => _connections.Count;
}
