namespace FjordWatch.Web.Models;

public sealed record AgentRequest(string Message, string? ConversationId);

public sealed record AgentResponse(
    string Reply,
    IReadOnlyList<Citation> Citations,
    string ConversationId);

public sealed record Citation(
    string Source,
    string Description,
    IReadOnlyDictionary<string, string> Parameters);
