using System.Text;
using ComplianceCopilot.Agent.Rag;
using ComplianceCopilot.Shared.Agent;
using OllamaSharp.Models.Chat;

namespace ComplianceCopilot.Agent.Orchestration;

/// <summary>
/// Calls the chat model to propose a structured Intent from a natural-language query. The
/// LLM's job stops at proposing JSON text - IntentParser (deterministic, unit-tested against
/// fixed sample outputs, no live model needed) decides what that text actually means.
/// </summary>
public sealed class IntentExtractor(OllamaClients ollama)
{
    private const string SystemPrompt = """
        Analyse the user's question about a company registration/compliance platform and
        respond with ONLY a JSON object (no other text, no markdown fences) in this exact shape:

        {
          "needsGeneralKnowledge": true or false,
          "toolIntent": "None" or "ListCompanies" or "ComplianceStatus" or "Document",
          "companyName": string or null,
          "documentType": string or null
        }

        Field meanings:
        - needsGeneralKnowledge: true if answering requires general compliance/regulatory
          knowledge (e.g. what a rule means, what happens if something is missed).
        - toolIntent: "ComplianceStatus" if the user asks about a specific company's filing
          deadlines or obligations; "Document" if they ask for a specific document;
          "ListCompanies" if they ask what companies they have; "None" if no user-specific
          data is needed at all.
        - companyName: the company name exactly as the user wrote it, or null if none mentioned.
        - documentType: the kind of document requested (e.g. "Constitution"), or null.
        """;

    public async Task<Intent> ExtractAsync(string query, CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = ollama.Chat.SelectedModel,
            Messages =
            [
                new Message(ChatRole.System, SystemPrompt),
                new Message(ChatRole.User, query),
            ],
            Stream = false,
        };

        var sb = new StringBuilder();
        await foreach (var response in ollama.Chat.ChatAsync(request, ct))
        {
            if (response?.Message?.Content is { } content)
                sb.Append(content);
        }

        return IntentParser.Parse(sb.ToString());
    }
}
