namespace ComplianceCopilot.Shared.Domain;

/// <summary>The computed view of a company's compliance status as of a specific date.</summary>
public sealed record ComplianceStatusView(
    string CompanyId,
    DateOnly NextAnnualReviewDue,
    string Label,
    IReadOnlyList<Obligation> OverdueObligations,
    IReadOnlyList<Obligation> UpcomingObligations);

/// <summary>
/// Pure, deterministic evaluation of a <see cref="ComplianceStatus"/> against a reference date.
/// Takes "asOf" as a plain parameter rather than reading the clock itself, so it's trivially
/// unit-testable without any DI/TimeProvider machinery — the caller (the MCP server) is
/// responsible for sourcing "now" from an injected <see cref="TimeProvider"/>.
/// </summary>
public static class ComplianceStatusEvaluator
{
    public static ComplianceStatusView Evaluate(ComplianceStatus status, DateOnly asOf)
    {
        var overdue = status.Obligations.Where(o => o.DueDate < asOf).ToList();
        var upcoming = status.Obligations.Where(o => o.DueDate >= asOf).ToList();

        return new ComplianceStatusView(
            status.CompanyId,
            status.NextAnnualReviewDue,
            Label: overdue.Count > 0 ? "Overdue" : "Active",
            overdue,
            upcoming);
    }
}
