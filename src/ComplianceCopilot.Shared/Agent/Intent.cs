namespace ComplianceCopilot.Shared.Agent;

public enum ToolIntentKind
{
    None,
    ListCompanies,
    ComplianceStatus,
    Document,
}

/// <summary>
/// Structured intent proposed by the LLM from a natural-language query. The LLM's role stops
/// here - it proposes this shape, and only this shape. What happens next (IntentParser,
/// QueryRouter) is deterministic code, not further LLM judgement.
/// </summary>
public sealed record Intent(bool NeedsRag, ToolIntentKind ToolIntent, string? CompanyName, string? DocumentType);
