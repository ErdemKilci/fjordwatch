namespace FjordWatch.Agent.Tools;

/// <summary>
/// A structured tool the agent can call. Each invocation returns a textual
/// summary the model can reference plus a <see cref="Citation"/> the API
/// surfaces back to the UI.
/// </summary>
public interface IAgentTool
{
    /// <summary>Stable identifier used by the orchestrator and the system prompt.</summary>
    string Name { get; }

    /// <summary>One-line description for the system prompt.</summary>
    string Description { get; }

    /// <summary>Parameter schema description for the system prompt.</summary>
    string ParameterSchema { get; }

    Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct);
}

public sealed record ToolResult(string Summary, Citation Citation);
