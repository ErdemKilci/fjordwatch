namespace FjordWatch.Agent.Rag;

public interface IEmbeddingProvider
{
    /// <summary>Vector dimension; cached at startup so the retriever knows the index width.</summary>
    int Dimension { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}
