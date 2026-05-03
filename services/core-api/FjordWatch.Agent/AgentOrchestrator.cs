using System.Globalization;
using System.Text;
using System.Text.Json;
using FjordWatch.Agent.Tools;

namespace FjordWatch.Agent;

/// <summary>
/// Drives the chat-completion + tool loop.
///
/// Strategy: a one-shot tool dispatch.
/// 1. Send the user's message to the chat provider with a system prompt that
///    enumerates the tools and their parameter schemas, and asks the model to
///    respond with a JSON tool call when it needs data.
/// 2. If the model emits a JSON object with a known tool, run it, append the
///    summary as a tool message, and ask the model for the final answer.
/// 3. The final answer is returned with the citations accumulated from any
///    tool calls in this turn.
///
/// This is deliberately simpler than full agentic loops: one tool call per
/// turn keeps latency predictable on Ollama (where a single 8B-class call is
/// already 2–5s on CPU) and avoids long-running plans we cannot interrupt
/// from the UI.
/// </summary>
public sealed class AgentOrchestrator : IAgent
{
    private readonly IChatProvider _provider;
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public AgentOrchestrator(IChatProvider provider, IEnumerable<IAgentTool> tools)
    {
        _provider = provider;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    public async Task<AgentResponse> AnswerAsync(AgentRequest request, CancellationToken ct)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString("N");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt()),
            new(ChatRole.User, request.Message),
        };

        var citations = new List<Citation>();
        var firstReply = await _provider.CompleteAsync(messages, ct);

        if (TryParseToolCall(firstReply, out var toolName, out var toolArgs) &&
            _tools.TryGetValue(toolName, out var tool))
        {
            var toolResult = await tool.InvokeAsync(toolArgs, ct);
            citations.Add(toolResult.Citation);
            messages.Add(new ChatMessage(ChatRole.Assistant, firstReply));
            messages.Add(new ChatMessage(ChatRole.Tool, $"tool {toolName} -> {toolResult.Summary}"));
            messages.Add(new ChatMessage(ChatRole.User,
                "Using only the tool result above, write the final answer in plain prose. " +
                "If the tool returned no rows, say so plainly. " +
                "Do not invent vessels, MMSIs, names, or coordinates."));
            var finalReply = await _provider.CompleteAsync(messages, ct);
            return new AgentResponse(finalReply, citations, conversationId);
        }

        return new AgentResponse(firstReply, citations, conversationId);
    }

    public static bool TryParseToolCall(
        string reply,
        out string toolName,
        out IReadOnlyDictionary<string, string> args)
    {
        toolName = "";
        args = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        var firstBrace = reply.IndexOf('{');
        var lastBrace = reply.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return false;
        }
        var json = reply[firstBrace..(lastBrace + 1)];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            toolName = toolEl.GetString() ?? "";
            if (string.IsNullOrEmpty(toolName)) return false;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (doc.RootElement.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsEl.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => prop.Value.GetRawText(),
                    };
                }
            }
            args = dict;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are FjordWatch, a maritime intelligence assistant for the Norwegian coast.");
        sb.AppendLine();
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- Never invent or guess MMSI numbers, vessel names, coordinates, dates, or scores.");
        sb.AppendLine("- If a tool returns no results, say so plainly.");
        sb.AppendLine("- Cite the exact tool you used; the application surfaces these citations.");
        sb.AppendLine("- Answer in the user's language (English or Norwegian).");
        sb.AppendLine();
        sb.AppendLine("To answer questions about live data or regulations, call exactly one tool by replying with");
        sb.AppendLine("a single JSON object on its own line:");
        sb.AppendLine("  {\"tool\": \"<name>\", \"args\": { ...key/value pairs... }}");
        sb.AppendLine("After you receive the tool result, write the final answer in plain prose.");
        sb.AppendLine();
        sb.AppendLine("Available tools:");
        var i = 1;
        foreach (var tool in _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append(". ").Append(tool.Name).Append(" — ").AppendLine(tool.Description);
            sb.Append("   args: ").AppendLine(tool.ParameterSchema);
            i++;
        }
        return sb.ToString();
    }
}
