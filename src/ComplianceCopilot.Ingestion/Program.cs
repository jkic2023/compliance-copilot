using ComplianceCopilot.Shared.Rag;
using OllamaSharp;

// One-shot dev tool, not a long-lived host - run from the repository root:
//   dotnet run --project src/ComplianceCopilot.Ingestion
// Always does a full rebuild: re-chunks every source file and re-embeds every chunk, then
// overwrites rag-index.json. Simpler and more honest than a partial/incremental update for a
// corpus this small, and it's the same command whether you're building the index the first
// time or rebuilding it after a source page changed.

const string SourceDir = "content/raw";
const string IndexPath = "rag-index.json";
const string EmbeddingModel = "nomic-embed-text";
const string OllamaBaseUrl = "http://localhost:11434";

if (!Directory.Exists(SourceDir))
{
    Console.Error.WriteLine($"Source directory '{SourceDir}' not found - run this tool from the repository root.");
    return 1;
}

var sourceFiles = Directory.GetFiles(SourceDir, "*.md").OrderBy(f => f).ToList();
if (sourceFiles.Count == 0)
{
    Console.Error.WriteLine($"No .md source files found under '{SourceDir}'.");
    return 1;
}

Console.WriteLine($"Found {sourceFiles.Count} source file(s) under {SourceDir}.");

var allChunks = new List<RagChunk>();
foreach (var file in sourceFiles)
{
    var sourceId = Path.GetFileNameWithoutExtension(file);
    var text = await File.ReadAllTextAsync(file);
    var chunks = DocumentChunker.ChunkMarkdownBySection(sourceId, text);
    Console.WriteLine($"  {sourceId}: {chunks.Count} chunk(s)");
    allChunks.AddRange(chunks);
}

Console.WriteLine($"Total chunks: {allChunks.Count}. Embedding via '{EmbeddingModel}' at {OllamaBaseUrl}...");

var ollama = new OllamaApiClient(new Uri(OllamaBaseUrl)) { SelectedModel = EmbeddingModel };

var entries = new List<RagIndexEntry>();
var dimension = 0;

foreach (var chunk in allChunks)
{
    var response = await ollama.EmbedAsync(chunk.Text);
    var vector = response.Embeddings[0];
    dimension = vector.Length;
    entries.Add(new RagIndexEntry(chunk, vector));
    Console.WriteLine($"  embedded {chunk.ChunkId} ({vector.Length} dims)");
}

var index = new RagIndex(
    EmbeddingModel: EmbeddingModel,
    EmbeddingDimension: dimension,
    GeneratedAt: DateTimeOffset.UtcNow,
    Entries: entries);

RagIndexStore.Save(index, IndexPath);

Console.WriteLine($"Saved {entries.Count} embedded chunks to '{IndexPath}'.");
return 0;
