namespace FjordWatch.Agent;

public sealed record AgentRequest(
    string Message,
    string? ConversationId);

public sealed record AgentResponse(
    string Reply,
    IReadOnlyList<Citation> Citations,
    string ConversationId);

public sealed record Citation(
    string Source,
    string Description,
    IReadOnlyDictionary<string, string> Parameters);

public interface IAgent
{
    Task<AgentResponse> AnswerAsync(AgentRequest request, CancellationToken ct);
}
