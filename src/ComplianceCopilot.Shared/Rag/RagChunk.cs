namespace ComplianceCopilot.Shared.Rag;

/// <summary>
/// One retrievable unit of the RAG corpus. ChunkId is stable and citable
/// ("SourceId#N") - this is what a grounded answer points back to.
/// </summary>
public sealed record RagChunk(string ChunkId, string SourceId, string Heading, string Text);
