using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FjordWatch.Agent.Providers;

public sealed class OllamaChatProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaChatProvider(HttpClient http, string model)
    {
        _http = http;
        _model = model;
    }

    public string Name => "ollama";
    public string Model => _model;

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var body = new
        {
            model = _model,
            stream = false,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Content,
            }).ToList(),
        };
        using var resp = await _http.PostAsJsonAsync("api/chat", body, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var parsed = await resp.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct).ConfigureAwait(false);
        return parsed?.Message?.Content ?? string.Empty;
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
