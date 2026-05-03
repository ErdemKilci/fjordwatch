using System.Net.Http.Json;
using FjordWatch.Web.Models;

namespace FjordWatch.Web.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<VesselDto>> GetVesselsAsync(
        double west, double south, double east, double north, CancellationToken ct = default)
    {
        var bbox = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{west},{south},{east},{north}");
        var path = $"vessels?bbox={Uri.EscapeDataString(bbox)}";
        var result = await _http.GetFromJsonAsync<IReadOnlyList<VesselDto>>(path, ct).ConfigureAwait(false);
        return result ?? [];
    }

    public Task<VesselDto?> GetVesselAsync(long mmsi, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<VesselDto>($"vessels/{mmsi}", ct);

    public Task<GeoJsonLineString?> GetTrackAsync(long mmsi, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<GeoJsonLineString>($"vessels/{mmsi}/track", ct);

    public async Task<IReadOnlyList<AnomalyDto>> GetAnomaliesAsync(
        DateTimeOffset? since = null,
        float? minScore = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (since.HasValue)
        {
            query.Add($"since={Uri.EscapeDataString(since.Value.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture))}");
        }
        if (minScore.HasValue)
        {
            query.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"minScore={minScore.Value}"));
        }
        if (limit.HasValue)
        {
            query.Add($"limit={limit.Value}");
        }
        var path = "anomalies" + (query.Count == 0 ? "" : "?" + string.Join('&', query));
        var result = await _http.GetFromJsonAsync<IReadOnlyList<AnomalyDto>>(path, ct).ConfigureAwait(false);
        return result ?? [];
    }
}
