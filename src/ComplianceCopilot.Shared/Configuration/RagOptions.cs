namespace ComplianceCopilot.Shared.Configuration;

/// <summary>Config section "Rag" - retrieval tuning, kept out of code so it's changeable without a rebuild.</summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public string IndexPath { get; set; } = "rag-index.json";
    public string SourceContentPath { get; set; } = "content/raw";
    public int TopK { get; set; } = 4;
    public double SimilarityThreshold { get; set; } = 0.55;
}
