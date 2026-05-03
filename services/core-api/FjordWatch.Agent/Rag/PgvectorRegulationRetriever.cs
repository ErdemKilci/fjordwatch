using System.Globalization;
using System.Text;
using Dapper;
using Npgsql;

namespace FjordWatch.Agent.Rag;

public sealed class PgvectorRegulationRetriever : IRegulationRetriever
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingProvider _embedder;

    public PgvectorRegulationRetriever(NpgsqlDataSource dataSource, IEmbeddingProvider embedder)
    {
        _dataSource = dataSource;
        _embedder = embedder;
    }

    public async Task<IReadOnlyList<RegulationChunk>> SearchAsync(string query, int topK, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }
        var clampedK = Math.Clamp(topK, 1, 20);
        var embedding = await _embedder.EmbedAsync(query, ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT id, source, title, chunk_index AS chunkindex, text, language
            FROM regulation_chunks
            ORDER BY embedding <=> @embedding::vector
            LIMIT @limit";

        var rows = await conn.QueryAsync<RegulationChunk>(new CommandDefinition(
            sql,
            new
            {
                embedding = FormatVector(embedding),
                limit = clampedK,
            },
            cancellationToken: ct));

        return rows.ToList();
    }

    private static string FormatVector(float[] embedding)
    {
        var sb = new StringBuilder(2 + embedding.Length * 8);
        sb.Append('[');
        for (var i = 0; i < embedding.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(embedding[i].ToString("R", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
