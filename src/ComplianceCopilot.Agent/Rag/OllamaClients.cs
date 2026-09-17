using ComplianceCopilot.Shared.Configuration;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace ComplianceCopilot.Agent.Rag;

/// <summary>
/// Three separate OllamaApiClient instances - one per model - rather than one client with a
/// mutable SelectedModel property flipped back and forth between calls. Avoids any ambiguity
/// about which model a given call actually used, especially once concurrent calls are in play.
/// Built against the low-level ChatAsync/EmbedAsync API throughout, not the high-level Chat
/// wrapper - see docs/architecture.html for why (the wrapper silently returned nothing in an
/// early smoke test).
/// </summary>
public sealed class OllamaClients
{
    public OllamaApiClient Chat { get; }
    public OllamaApiClient Embedding { get; }

    /// <summary>
    /// Genuinely smaller/cheaper model (qwen2.5:1.5b, not qwen2.5:7b-instruct reused) used only
    /// for the grounding-correction regeneration pass in RagAnswerGenerator.RegenerateAsync -
    /// makes the "second pass" of hallucination mitigation a real, distinct check rather than
    /// asking the same model that made the mistake to mark its own homework.
    /// </summary>
    public OllamaApiClient Verifier { get; }

    public OllamaClients(IOptions<OllamaOptions> options)
    {
        var opts = options.Value;
        var baseUri = new Uri(opts.BaseUrl);

        Chat = new OllamaApiClient(baseUri) { SelectedModel = opts.ChatModel };
        Embedding = new OllamaApiClient(baseUri) { SelectedModel = opts.EmbeddingModel };
        Verifier = new OllamaApiClient(baseUri) { SelectedModel = opts.VerifierModel };
    }
}
