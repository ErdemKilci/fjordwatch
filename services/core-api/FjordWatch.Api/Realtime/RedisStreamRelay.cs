using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace FjordWatch.Api.Realtime;

/// <summary>
/// Background service that reads decoded AIS messages from the Redis Stream
/// produced by the Rust ingestion service, applies each connection's viewport
/// + rate-limit filter, and pushes <c>positionUpdate</c> events to the SignalR
/// hub. Crashes during the read are recovered by reopening the stream from
/// the last successfully delivered ID.
/// </summary>
public sealed partial class RedisStreamRelay : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "redis stream relay starting on key {Key}")]
    private partial void LogStarting(string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "stream payload deserialize failed: {Payload}")]
    private partial void LogPayloadDeserializeFailed(Exception ex, string payload);

    [LoggerMessage(Level = LogLevel.Warning, Message = "stream read failed; backing off")]
    private partial void LogStreamReadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "client send failed; will skip on next tick")]
    private partial void LogClientSendFailed(Exception ex);

    private readonly IConnectionMultiplexer _redis;
    private readonly ConnectionRegistry _registry;
    private readonly IHubContext<VesselsHub> _hub;
    private readonly ILogger<RedisStreamRelay> _logger;
    private readonly RedisStreamRelayOptions _options;
    private readonly Counter<long> _messagesIn;
    private readonly Counter<long> _messagesOut;
    private readonly Counter<long> _drops;

    private string _lastId = "0-0";

    public RedisStreamRelay(
        IConnectionMultiplexer redis,
        ConnectionRegistry registry,
        IHubContext<VesselsHub> hub,
        ILogger<RedisStreamRelay> logger,
        RedisStreamRelayOptions options,
        IMeterFactory meterFactory)
    {
        _redis = redis;
        _registry = registry;
        _hub = hub;
        _logger = logger;
        _options = options;

        var meter = meterFactory.Create("FjordWatch.Api.RedisRelay");
        _messagesIn = meter.CreateCounter<long>("relay_messages_in_total");
        _messagesOut = meter.CreateCounter<long>("relay_messages_out_total");
        _drops = meter.CreateCounter<long>("relay_messages_dropped_total");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        LogStarting(_options.StreamKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadAsync(
                    _options.StreamKey,
                    _lastId,
                    _options.BatchSize);

                if (entries.Length == 0)
                {
                    await Task.Delay(_options.IdlePoll, stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    _lastId = entry.Id!;
                    var payload = entry["payload"];
                    if (payload.IsNullOrEmpty)
                    {
                        continue;
                    }

                    StreamMessage? msg;
                    try
                    {
                        msg = JsonSerializer.Deserialize<StreamMessage>(payload!);
                    }
                    catch (JsonException ex)
                    {
                        LogPayloadDeserializeFailed(ex, payload.ToString());
                        _drops.Add(1);
                        continue;
                    }

                    if (msg is null || msg.Position is null)
                    {
                        continue;
                    }

                    _messagesIn.Add(1);
                    await DispatchAsync(msg, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogStreamReadFailed(ex);
                _drops.Add(1);
                await Task.Delay(_options.ErrorBackoff, stoppingToken);
            }
        }
    }

    private async Task DispatchAsync(StreamMessage msg, CancellationToken ct)
    {
        var pos = msg.Position!;
        var now = DateTimeOffset.UtcNow;

        foreach (var (connectionId, filter) in _registry.Snapshot())
        {
            if (!filter.ShouldSend(msg.Mmsi, pos.Longitude, pos.Latitude, now))
            {
                continue;
            }

            try
            {
                await _hub.Clients.Client(connectionId).SendAsync(
                    "positionUpdate",
                    new
                    {
                        mmsi = msg.Mmsi,
                        ts = msg.Timestamp,
                        latitude = pos.Latitude,
                        longitude = pos.Longitude,
                        sog = pos.SpeedOverGround,
                        cog = pos.CourseOverGround,
                        heading = pos.TrueHeading,
                    },
                    ct);
                _messagesOut.Add(1);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogClientSendFailed(ex);
            }
        }
    }
}

public sealed record RedisStreamRelayOptions
{
    public string StreamKey { get; init; } = "ais:positions";
    public int BatchSize { get; init; } = 100;
    public TimeSpan IdlePoll { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ErrorBackoff { get; init; } = TimeSpan.FromSeconds(2);
}
