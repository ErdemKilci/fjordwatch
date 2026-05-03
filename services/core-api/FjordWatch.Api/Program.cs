using System.Text.Json;
using System.Text.Json.Serialization;
using FjordWatch.Api.Endpoints;
using FjordWatch.Api.Realtime;
using FjordWatch.Domain;
using FjordWatch.Infrastructure;
using Npgsql;
using OpenTelemetry.Metrics;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var databaseUrl = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("DATABASE_URL is required");

builder.Services.AddSingleton(_ =>
    new NpgsqlDataSourceBuilder(NpgsqlConnectionStringConverter.ToKeyValue(databaseUrl)).Build());
builder.Services.AddScoped<IVesselRepository, PostgresVesselRepository>();
builder.Services.AddScoped<IAnomalyRepository, PostgresAnomalyRepository>();

var redisUrl = builder.Configuration["REDIS_URL"] ?? "redis://redis:6379/0";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(RedisUrlConverter.ToConfigurationString(redisUrl)));

builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton(new RedisStreamRelayOptions
{
    StreamKey = builder.Configuration["AIS_STREAM"] ?? "ais:positions",
});
builder.Services.AddHostedService<RedisStreamRelay>();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

var corsOrigins = (builder.Configuration["CORS_ORIGINS"] ?? "http://localhost:5000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("FjordWatch.Api.RedisRelay")
        .AddPrometheusExporter());

var app = builder.Build();

app.UseCors();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/readyz", async (IConnectionMultiplexer redis, NpgsqlDataSource pg, CancellationToken ct) =>
{
    try
    {
        await using var conn = await pg.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        var pong = await redis.GetDatabase().PingAsync();
        return Results.Ok(new { status = "ready", redisLatencyMs = pong.TotalMilliseconds });
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        return Results.Json(new { status = "not_ready", error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPrometheusScrapingEndpoint("/metrics");

app.MapVesselEndpoints();
app.MapAnomalyEndpoints();

app.MapHub<VesselsHub>("/hubs/vessels");

app.Run();

public partial class Program;
