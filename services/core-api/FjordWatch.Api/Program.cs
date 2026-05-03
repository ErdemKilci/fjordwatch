using System.Text.Json;
using System.Text.Json.Serialization;
using FjordWatch.Agent;
using FjordWatch.Agent.Providers;
using FjordWatch.Agent.Rag;
using FjordWatch.Agent.Tools;
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
builder.Services.AddScoped<ISarDetectionRepository, PostgresSarDetectionRepository>();

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

// ---- Agent ----------------------------------------------------------------
var llmProvider = builder.Configuration["LLM_PROVIDER"]?.ToLowerInvariant() ?? "ollama";

builder.Services.AddHttpClient("agent-chat-ollama", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OLLAMA_HOST"] ?? "http://ollama:11434/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient("agent-chat-azure", client =>
{
    var endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"];
    if (!string.IsNullOrEmpty(endpoint))
    {
        client.BaseAddress = new Uri(endpoint);
    }
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<IChatProvider>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    if (llmProvider == "azure_openai")
    {
        var deployment = builder.Configuration["AZURE_OPENAI_DEPLOYMENT"]
            ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT is required when LLM_PROVIDER=azure_openai");
        var apiKey = builder.Configuration["AZURE_OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is required when LLM_PROVIDER=azure_openai");
        return new AzureOpenAIChatProvider(factory.CreateClient("agent-chat-azure"), deployment, apiKey);
    }
    var model = builder.Configuration["OLLAMA_MODEL"] ?? "llama3.1:8b-instruct-q4_K_M";
    return new OllamaChatProvider(factory.CreateClient("agent-chat-ollama"), model);
});

builder.Services.AddHttpClient("embedding", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["EMBEDDING_URL"] ?? "http://embedding:8004/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IEmbeddingProvider>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var dim = int.Parse(builder.Configuration["EMBEDDING_DIMENSION"] ?? "1024", System.Globalization.CultureInfo.InvariantCulture);
    return new HttpEmbeddingProvider(factory.CreateClient("embedding"), dim);
});
builder.Services.AddScoped<IRegulationRetriever, PgvectorRegulationRetriever>();
builder.Services.AddScoped<IAgentTool, NearestVesselsTool>();
builder.Services.AddScoped<IAgentTool, VesselHistoryTool>();
builder.Services.AddScoped<IAgentTool, RecentAnomaliesTool>();
builder.Services.AddScoped<IAgentTool, DarkVesselsTool>();
builder.Services.AddScoped<IAgentTool, SearchRegulationsTool>();
builder.Services.AddScoped<IAgent, AgentOrchestrator>();

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
app.MapSarEndpoints();
app.MapAgentEndpoints();

app.MapHub<VesselsHub>("/hubs/vessels");

app.Run();

public partial class Program;
