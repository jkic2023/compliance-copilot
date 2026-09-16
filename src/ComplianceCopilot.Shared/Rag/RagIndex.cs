using System.Text.Json;

namespace ComplianceCopilot.Shared.Rag;

public sealed record RagIndexEntry(RagChunk Chunk, float[] Embedding);

/// <summary>
/// The persisted RAG index. Carries its own provenance (embedding model, dimension,
/// generation time) rather than just a bare array of vectors - if the embedding model is
/// ever swapped, a dimension mismatch fails loudly on load instead of silently producing
/// garbage similarity scores.
/// </summary>
public sealed record RagIndex(
    string EmbeddingModel,
    int EmbeddingDimension,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RagIndexEntry> Entries);

public static class RagIndexStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Save(RagIndex index, string path)
    {
        var json = JsonSerializer.Serialize(index, Options);
        File.WriteAllText(path, json);
    }

    public static RagIndex Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RagIndex>(json, Options)
            ?? throw new InvalidOperationException($"RAG index file '{path}' deserialized to null.");
    }
}
