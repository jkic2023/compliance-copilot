using ComplianceCopilot.Shared.Agent;
using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Tests;

public class ToolResultCrossCheckerTests
{
    [Fact]
    public void ExtractKeyValues_OverdueStatus_ReturnsOverdueObligationDatesOnly()
    {
        var status = new ComplianceStatusView(
            "beta-1", new DateOnly(2026, 11, 5), "Overdue",
            OverdueObligations: [new Obligation("Annual review lodgement", new DateOnly(2026, 8, 20))],
            UpcomingObligations: []);

        var values = ToolResultCrossChecker.ExtractKeyValues(status);

        // NextAnnualReviewDue is never mentioned by ToolResultFormatter on the Overdue branch,
        // so it must not appear here either - checking it would be a false positive, not a real drift.
        Assert.Equal(["2026-08-20"], values);
    }

    [Fact]
    public void ExtractKeyValues_ActiveStatus_ReturnsNextReviewDateOnly()
    {
        var status = new ComplianceStatusView(
            "acme-1", new DateOnly(2026, 11, 5), "Active",
            OverdueObligations: [],
            UpcomingObligations: [new Obligation("Annual review lodgement", new DateOnly(2026, 11, 5))]);

        var values = ToolResultCrossChecker.ExtractKeyValues(status);

        Assert.Equal(["2026-11-05"], values);
    }

    [Fact]
    public void Check_ComposedAnswerContainsAllExpectedValues_IsConsistent()
    {
        var result = ToolResultCrossChecker.Check(
            "Acme Pty Ltd's next annual review is due 2026-11-05.",
            ["2026-11-05"]);

        Assert.True(result.IsConsistent);
        Assert.Empty(result.DriftedValues);
    }

    [Fact]
    public void Check_ComposedAnswerParaphrasesDateAway_IsFlaggedAsDrifted()
    {
        var result = ToolResultCrossChecker.Check(
            "Acme Pty Ltd's next annual review is due in early November.",
            ["2026-11-05"]);

        Assert.False(result.IsConsistent);
        Assert.Equal(["2026-11-05"], result.DriftedValues);
    }

    [Fact]
    public void ExtractKeyValues_Document_ReturnsIssuedDateAndDocumentType()
    {
        var document = new CompanyDocument("acme-1", "Constitution", new DateOnly(2020, 3, 1), "summary text");

        var values = ToolResultCrossChecker.ExtractKeyValues(document);

        Assert.Equal(["2020-03-01", "Constitution"], values);
    }
}
