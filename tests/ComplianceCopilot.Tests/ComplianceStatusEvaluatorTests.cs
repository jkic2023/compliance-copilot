using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Tests;

/// <summary>
/// Proves the overdue-vs-upcoming judgement is computed from a passed-in reference date, not
/// tied to the real clock or baked into the data - the same fixture data evaluates differently
/// depending on "asOf", which is exactly the point.
/// </summary>
public class ComplianceStatusEvaluatorTests
{
    private static readonly ComplianceStatus Beta = new(
        CompanyId: "beta-1",
        NextAnnualReviewDue: new DateOnly(2026, 8, 20),
        Obligations:
        [
            new Obligation("Annual review lodgement", new DateOnly(2026, 8, 20)),
            new Obligation("ASIC annual review fee payment", new DateOnly(2026, 8, 20))
        ]);

    [Fact]
    public void Evaluate_WhenAsOfIsAfterDueDate_ReportsOverdue()
    {
        var result = ComplianceStatusEvaluator.Evaluate(Beta, asOf: new DateOnly(2026, 9, 16));

        Assert.Equal("Overdue", result.Label);
        Assert.Equal(2, result.OverdueObligations.Count);
        Assert.Empty(result.UpcomingObligations);
    }

    [Fact]
    public void Evaluate_WhenAsOfIsBeforeDueDate_ReportsActiveWithUpcomingObligations()
    {
        var result = ComplianceStatusEvaluator.Evaluate(Beta, asOf: new DateOnly(2026, 7, 1));

        Assert.Equal("Active", result.Label);
        Assert.Empty(result.OverdueObligations);
        Assert.Equal(2, result.UpcomingObligations.Count);
    }

    [Fact]
    public void Evaluate_WhenAsOfEqualsDueDate_TreatsItAsUpcomingNotOverdue()
    {
        // Due "on" the date is not yet overdue - overdue means asOf is strictly after DueDate.
        var result = ComplianceStatusEvaluator.Evaluate(Beta, asOf: new DateOnly(2026, 8, 20));

        Assert.Equal("Active", result.Label);
        Assert.Equal(2, result.UpcomingObligations.Count);
    }
}
