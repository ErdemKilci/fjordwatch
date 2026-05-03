namespace FjordWatch.Agent.Rag;

public interface IRegulationRetriever
{
    Task<IReadOnlyList<RegulationChunk>> SearchAsync(string query, int topK, CancellationToken ct);
}

public sealed record RegulationChunk(
    long Id,
    string Source,
    string Title,
    int ChunkIndex,
    string Text,
    string Language);
