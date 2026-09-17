using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Shared.Agent;

/// <summary>Which raw tool values didn't survive, verbatim, into the LLM's composed answer.</summary>
public sealed record CrossCheckResult(bool IsConsistent, IReadOnlyList<string> DriftedValues);

/// <summary>
/// Deterministic tool-result cross-check: extracts the key values (dates, document type) from a
/// raw tool result and verifies each one still appears, character for character, in the LLM's
/// final composed answer. This exists specifically for the mixed-query path - a tool-only answer
/// is pure deterministic templating via <see cref="ToolResultFormatter"/> and never reaches an
/// LLM at all, so it has zero drift risk by construction and needs no check here. The mixed-query
/// composition step (AgentOrchestrator.ComposeAsync) is the one place a tool-derived fact passes
/// through an LLM asked to "rephrase and connect" it with the RAG answer, which is exactly where
/// a date or figure could silently drift (e.g. "2026-08-20" paraphrased as "in late August").
/// </summary>
public static class ToolResultCrossChecker
{
    /// <summary>
    /// Mirrors ToolResultFormatter.FormatComplianceStatus's own branching exactly, rather than
    /// dumping every date on the object - the compose LLM is only ever shown the formatted
    /// toolFact string, so an Overdue status's expected values are its overdue obligations'
    /// dates (what's actually in that string), not NextAnnualReviewDue, which the formatter
    /// never mentions on that branch. Checking a value the LLM was never given would be a false
    /// positive, not a real drift.
    /// </summary>
    public static IReadOnlyList<string> ExtractKeyValues(ComplianceStatusView status) =>
        status.Label == "Overdue"
            ? status.OverdueObligations.Select(o => o.DueDate.ToString("yyyy-MM-dd")).ToList()
            : [status.NextAnnualReviewDue.ToString("yyyy-MM-dd")];

    public static IReadOnlyList<string> ExtractKeyValues(CompanyDocument document) =>
        [document.IssuedDate.ToString("yyyy-MM-dd"), document.DocumentType];

    public static CrossCheckResult Check(string composedAnswer, IReadOnlyList<string> expectedValues)
    {
        var drifted = expectedValues
            .Where(value => !composedAnswer.Contains(value, StringComparison.Ordinal))
            .ToList();

        return new CrossCheckResult(drifted.Count == 0, drifted);
    }
}
