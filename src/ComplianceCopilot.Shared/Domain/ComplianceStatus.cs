namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// A company's raw compliance data — obligations only, no pre-judged "overdue" label.
/// Whether an obligation is overdue depends on "now", so that judgement is computed at query
/// time by <see cref="ComplianceStatusEvaluator"/> against an injected reference date, never
/// baked into the fixture (a static "Overdue" string here would silently go stale/wrong the
/// moment real calendar time passed the fixture's dates).
/// </summary>
public sealed record ComplianceStatus(
    string CompanyId,
    DateOnly NextAnnualReviewDue,
    IReadOnlyList<Obligation> Obligations);
