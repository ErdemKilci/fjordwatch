using System.Globalization;
using FjordWatch.Domain;

namespace FjordWatch.Agent.Tools;

public sealed class VesselHistoryTool : IAgentTool
{
    private readonly IVesselRepository _repo;

    public VesselHistoryTool(IVesselRepository repo)
    {
        _repo = repo;
    }

    public string Name => "vessel_history";
    public string Description => "Trajectory summary for a single vessel over the last N hours.";
    public string ParameterSchema => "{ mmsi: integer, hours: integer (default 24, max 48) }";

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!args.TryGetValue("mmsi", out var rawMmsi) || !long.TryParse(rawMmsi, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mmsi))
        {
            return Failure("invalid mmsi");
        }
        var hours = 24;
        if (args.TryGetValue("hours", out var rawHours) && int.TryParse(rawHours, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            hours = Math.Clamp(parsed, 1, 48);
        }

        var to = DateTimeOffset.UtcNow;
        var from = to - TimeSpan.FromHours(hours);
        var track = await _repo.GetTrackAsync(mmsi, from, to, ct);

        var summary = track.Points.Count == 0
            ? $"No positions recorded for MMSI {mmsi} in the last {hours} hours."
            : $"MMSI {mmsi} has {track.Points.Count} positions over the last {hours} hours, " +
              $"from {track.Start:u} to {track.End:u}.";

        return new ToolResult(
            summary,
            new Citation(
                Source: Name,
                Description: $"{track.Points.Count} positions",
                Parameters: new Dictionary<string, string>
                {
                    ["mmsi"] = mmsi.ToString(CultureInfo.InvariantCulture),
                    ["hours"] = hours.ToString(CultureInfo.InvariantCulture),
                }));
    }

    private static ToolResult Failure(string reason) =>
        new(reason, new Citation(Source: "vessel_history", Description: $"error: {reason}", Parameters: new Dictionary<string, string>()));
}
