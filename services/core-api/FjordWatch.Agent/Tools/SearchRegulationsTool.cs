using FjordWatch.Agent.Rag;

namespace FjordWatch.Agent.Tools;

public sealed class SearchRegulationsTool : IAgentTool
{
    private readonly IRegulationRetriever _retriever;

    public SearchRegulationsTool(IRegulationRetriever retriever)
    {
        _retriever = retriever;
    }

    public string Name => "search_regulations";
    public string Description => "Semantic search over Norwegian maritime regulations and AIS reference materials. Use when the user asks about reporting requirements, vessel types, or rules.";
    public string ParameterSchema => "{ query: string, top_k: integer (default 5, max 20) }";

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            return new ToolResult(
                "missing query",
                new Citation(Source: Name, Description: "error: missing query", Parameters: new Dictionary<string, string>()));
        }

        var topK = 5;
        if (args.TryGetValue("top_k", out var rawK) && int.TryParse(rawK, out var parsed))
        {
            topK = Math.Clamp(parsed, 1, 20);
        }

        var chunks = await _retriever.SearchAsync(query, topK, ct);
        if (chunks.Count == 0)
        {
            return new ToolResult(
                "No matching regulation chunks. The corpus may not yet be ingested.",
                new Citation(Source: Name, Description: "0 matches", Parameters: new Dictionary<string, string> { ["query"] = query }));
        }

        var top = chunks[0];
        var summary = $"Top match: {top.Title} (chunk {top.ChunkIndex}). Excerpt: \"{Truncate(top.Text, 240)}\"";

        return new ToolResult(
            summary,
            new Citation(
                Source: Name,
                Description: $"{chunks.Count} chunks; top: {top.Title}",
                Parameters: new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["top_chunk_id"] = top.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["top_source"] = top.Source,
                }));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
