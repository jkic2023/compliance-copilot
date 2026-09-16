using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Shared.Agent;

/// <summary>
/// Deterministic, template-based formatting of a raw tool result into plain English - no LLM
/// call needed for a tool-only answer, since the fact is already exactly what the user asked
/// for. Zero hallucination risk here by construction: it's string interpolation over real data,
/// not generation.
/// </summary>
public static class ToolResultFormatter
{
    public static string FormatCompanyList(IReadOnlyList<Company> companies)
    {
        if (companies.Count == 0)
            return "You don't have any companies on file.";

        var names = string.Join(", ", companies.Select(c => c.Name));
        var noun = companies.Count == 1 ? "company" : "companies";
        return $"You have {companies.Count} {noun}: {names}.";
    }

    public static string FormatComplianceStatus(string companyName, ComplianceStatusView status)
    {
        if (status.Label == "Overdue")
        {
            var items = string.Join(
                "; ",
                status.OverdueObligations.Select(o => $"{o.Description} (was due {o.DueDate:yyyy-MM-dd})"));
            return $"{companyName}'s compliance status is Overdue: {items}.";
        }

        return $"{companyName}'s next annual review is due {status.NextAnnualReviewDue:yyyy-MM-dd} and its status is Active.";
    }

    public static string FormatDocument(CompanyDocument document)
    {
        return $"{document.DocumentType}, issued {document.IssuedDate:yyyy-MM-dd}: {document.ContentSummary}";
    }
}
