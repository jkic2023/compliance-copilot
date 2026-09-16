using ComplianceCopilot.Shared.Configuration;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace ComplianceCopilot.Agent.Rag;

/// <summary>
/// Two separate OllamaApiClient instances - one per model - rather than one client with a
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

    public OllamaClients(IOptions<OllamaOptions> options)
    {
        var opts = options.Value;
        var baseUri = new Uri(opts.BaseUrl);

        Chat = new OllamaApiClient(baseUri) { SelectedModel = opts.ChatModel };
        Embedding = new OllamaApiClient(baseUri) { SelectedModel = opts.EmbeddingModel };
    }
}
