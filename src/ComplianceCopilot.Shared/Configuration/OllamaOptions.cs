namespace ComplianceCopilot.Shared.Configuration;

/// <summary>Config section "Ollama" - model selection and connection settings, kept out of code.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen2.5:7b-instruct";
    public string VerifierModel { get; set; } = "qwen2.5:1.5b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int TimeoutSeconds { get; set; } = 30;
}
