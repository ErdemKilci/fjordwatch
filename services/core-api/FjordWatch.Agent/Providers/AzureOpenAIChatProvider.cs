using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FjordWatch.Agent.Providers;

/// <summary>
/// Direct REST client for Azure OpenAI chat completions. Avoids pulling in
/// the full Azure.AI.OpenAI SDK so the build artifact stays small; we only
/// use the chat-completions endpoint and a single header for auth.
/// </summary>
public sealed class AzureOpenAIChatProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly string _deployment;
    private readonly string _apiKey;
    private readonly string _apiVersion;

    public AzureOpenAIChatProvider(HttpClient http, string deployment, string apiKey, string apiVersion = "2024-10-21")
    {
        _http = http;
        _deployment = deployment;
        _apiKey = apiKey;
        _apiVersion = apiVersion;
    }

    public string Name => "azure_openai";
    public string Model => _deployment;

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var path = $"openai/deployments/{Uri.EscapeDataString(_deployment)}/chat/completions?api-version={Uri.EscapeDataString(_apiVersion)}";
        var body = new
        {
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Content,
            }).ToList(),
            temperature = 0.2,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("api-key", _apiKey);
        using var resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var parsed = await resp.Content.ReadFromJsonAsync<AzureResponse>(cancellationToken: ct).ConfigureAwait(false);
        return parsed?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    private sealed class AzureResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public AzureMessage? Message { get; set; }
    }

    private sealed class AzureMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
