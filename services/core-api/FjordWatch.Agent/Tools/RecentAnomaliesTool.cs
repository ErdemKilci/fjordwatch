using System.Globalization;
using FjordWatch.Domain;

namespace FjordWatch.Agent.Tools;

public sealed class RecentAnomaliesTool : IAgentTool
{
    private readonly IAnomalyRepository _repo;

    public RecentAnomaliesTool(IAnomalyRepository repo)
    {
        _repo = repo;
    }

    public string Name => "recent_anomalies";
    public string Description => "Recent anomaly scores above a threshold. Use when the user asks about unusual vessel behaviour.";
    public string ParameterSchema => "{ since_hours: integer (default 24, max 168), min_score: number 0..1 (default 0.5) }";

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var hours = 24;
        if (args.TryGetValue("since_hours", out var rawHours) && int.TryParse(rawHours, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            hours = Math.Clamp(parsed, 1, 168);
        }
        var minScore = 0.5f;
        if (args.TryGetValue("min_score", out var rawScore) && float.TryParse(rawScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
        {
            minScore = Math.Clamp(s, 0.0f, 1.0f);
        }

        var since = DateTimeOffset.UtcNow - TimeSpan.FromHours(hours);
        var rows = await _repo.ListAsync(since, minScore, limit: 100, ct);

        var summary = rows.Count == 0
            ? $"No anomalies above {minScore:F2} in the last {hours} hours."
            : $"{rows.Count} anomalies above score {minScore:F2} in the last {hours} hours. " +
              "Top vessels: " + string.Join(", ", rows.Take(5).Select(r => $"MMSI {r.Mmsi} ({r.Score:F2})"));

        return new ToolResult(
            summary,
            new Citation(
                Source: Name,
                Description: $"{rows.Count} anomalies",
                Parameters: new Dictionary<string, string>
                {
                    ["since_hours"] = hours.ToString(CultureInfo.InvariantCulture),
                    ["min_score"] = minScore.ToString("F2", CultureInfo.InvariantCulture),
                }));
    }
}
