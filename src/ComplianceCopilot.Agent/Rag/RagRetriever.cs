using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Rag;
using Microsoft.Extensions.Options;
using OllamaSharp.Models;

namespace ComplianceCopilot.Agent.Rag;

public sealed record RetrievedChunk(RagChunk Chunk, double Similarity);

/// <summary>
/// Brute-force cosine top-k over the in-memory index - no vector DB, deliberately, for a
/// corpus this small (see docs/project-overview.html for the rationale). Below
/// RagOptions.SimilarityThreshold, a chunk simply isn't returned at all: an empty result is
/// how the rest of the pipeline (commit 7+) knows to abstain on the knowledge portion of an
/// answer rather than being handed weakly-relevant chunks and asked to make the best of it.
/// </summary>
public sealed class RagRetriever
{
    private readonly RagIndex _index;
    private readonly OllamaClients _ollama;
    private readonly RagOptions _options;

    public RagRetriever(RagIndex index, OllamaClients ollama, IOptions<RagOptions> options)
    {
        _index = index;
        _ollama = ollama;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, CancellationToken ct = default)
    {
        var response = await _ollama.Embedding.EmbedAsync(
            new EmbedRequest { Model = _ollama.Embedding.SelectedModel, Input = [query] },
            ct);
        var queryVector = response.Embeddings[0];

        return _index.Entries
            .Select(e => new RetrievedChunk(e.Chunk, VectorMath.CosineSimilarity(queryVector, e.Embedding)))
            .Where(r => r.Similarity >= _options.SimilarityThreshold)
            .OrderByDescending(r => r.Similarity)
            .Take(_options.TopK)
            .ToList();
    }
}
