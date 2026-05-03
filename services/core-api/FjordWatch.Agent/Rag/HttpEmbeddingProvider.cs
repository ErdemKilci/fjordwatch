using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FjordWatch.Agent.Rag;

/// <summary>
/// HTTP client for the Python embedding service in <c>services/embedding</c>.
/// </summary>
public sealed class HttpEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;

    public HttpEmbeddingProvider(HttpClient http, int dimension)
    {
        _http = http;
        Dimension = dimension;
    }

    public int Dimension { get; }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync("embed", new { text }, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var parsed = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct).ConfigureAwait(false);
        return parsed?.Embedding ?? [];
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
