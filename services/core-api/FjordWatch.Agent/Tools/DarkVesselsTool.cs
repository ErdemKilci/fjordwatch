using System.Globalization;
using FjordWatch.Domain;

namespace FjordWatch.Agent.Tools;

public sealed class DarkVesselsTool : IAgentTool
{
    private readonly ISarDetectionRepository _repo;

    public DarkVesselsTool(ISarDetectionRepository repo)
    {
        _repo = repo;
    }

    public string Name => "dark_vessels";
    public string Description => "SAR detections without a matching AIS broadcast (is_dark = TRUE) inside a bbox in the last N hours. Use when the user asks about dark vessels or unmatched SAR ships.";
    public string ParameterSchema => "{ west: number, south: number, east: number, north: number, since_hours: integer (default 24, max 168) }";

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!TryParseBox(args, out var bbox))
        {
            return Failure("invalid bounding box");
        }
        var hours = 24;
        if (args.TryGetValue("since_hours", out var rawHours) && int.TryParse(rawHours, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            hours = Math.Clamp(parsed, 1, 168);
        }
        var since = DateTimeOffset.UtcNow - TimeSpan.FromHours(hours);
        var rows = await _repo.ListAsync(bbox, since, onlyDark: true, limit: 100, ct);

        var summary = rows.Count == 0
            ? $"No dark vessels in the last {hours} hours inside the requested area."
            : $"{rows.Count} dark detections in the last {hours} hours. " +
              "Earliest: " + rows[^1].DetectedAt.ToString("u") + ", latest: " + rows[0].DetectedAt.ToString("u");

        return new ToolResult(
            summary,
            new Citation(
                Source: Name,
                Description: $"{rows.Count} dark detections",
                Parameters: new Dictionary<string, string>
                {
                    ["bbox"] = $"{bbox.West:F2},{bbox.South:F2},{bbox.East:F2},{bbox.North:F2}",
                    ["since_hours"] = hours.ToString(CultureInfo.InvariantCulture),
                }));
    }

    private static bool TryParseBox(IReadOnlyDictionary<string, string> args, out BoundingBox bbox)
    {
        bbox = default;
        if (!Try(args, "west", out var w) || !Try(args, "south", out var s) || !Try(args, "east", out var e) || !Try(args, "north", out var n))
        {
            return false;
        }
        var box = new BoundingBox(w, s, e, n);
        if (!box.IsValid)
        {
            return false;
        }
        bbox = box;
        return true;
    }

    private static bool Try(IReadOnlyDictionary<string, string> args, string key, out double value)
    {
        value = 0.0;
        return args.TryGetValue(key, out var raw) &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static ToolResult Failure(string reason) =>
        new(reason, new Citation(Source: "dark_vessels", Description: $"error: {reason}", Parameters: new Dictionary<string, string>()));
}
