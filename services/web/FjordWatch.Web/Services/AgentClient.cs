using System.Net.Http.Json;
using FjordWatch.Web.Models;

namespace FjordWatch.Web.Services;

public sealed class AgentClient
{
    private readonly HttpClient _http;

    public AgentClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<AgentResponse?> AskAsync(string message, string? conversationId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            "agent/chat",
            new AgentRequest(message, conversationId),
            ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<AgentResponse>(cancellationToken: ct).ConfigureAwait(false);
    }
}
