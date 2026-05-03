using FjordWatch.Web.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace FjordWatch.Web.Services;

/// <summary>
/// Thin wrapper around the SignalR <c>HubConnection</c> for the vessels hub.
/// Reconnects automatically; consumers subscribe to <see cref="OnPositionUpdate"/>
/// and call <see cref="SetViewportAsync"/> when the map moves.
/// </summary>
public sealed class VesselsHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public event Action<PositionUpdate>? OnPositionUpdate;
    public event Action<HubConnectionState>? OnStateChanged;

    public VesselsHubClient(Uri hubUri)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
            ])
            .Build();

        _connection.On<PositionUpdate>("positionUpdate", update => OnPositionUpdate?.Invoke(update));
        _connection.Reconnecting += _ => { OnStateChanged?.Invoke(HubConnectionState.Reconnecting); return Task.CompletedTask; };
        _connection.Reconnected += _ => { OnStateChanged?.Invoke(HubConnectionState.Connected); return Task.CompletedTask; };
        _connection.Closed += _ => { OnStateChanged?.Invoke(HubConnectionState.Disconnected); return Task.CompletedTask; };
    }

    public HubConnectionState State => _connection.State;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync(ct).ConfigureAwait(false);
            OnStateChanged?.Invoke(_connection.State);
        }
    }

    public Task SetViewportAsync(double west, double south, double east, double north, CancellationToken ct = default)
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            return Task.CompletedTask;
        }
        return _connection.InvokeAsync("SetViewport", west, south, east, north, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
