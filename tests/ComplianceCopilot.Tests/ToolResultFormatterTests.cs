using ComplianceCopilot.Shared.Agent;
using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Tests;

public class ToolResultFormatterTests
{
    [Fact]
    public void FormatCompanyList_Empty_ReturnsNoCompaniesMessage()
    {
        var result = ToolResultFormatter.FormatCompanyList([]);

        Assert.Equal("You don't have any companies on file.", result);
    }

    [Fact]
    public void FormatCompanyList_Multiple_JoinsNamesWithCorrectPluralisation()
    {
        IReadOnlyList<Company> companies =
        [
            new Company("acme-1", "u1", "Acme Pty Ltd", "abn1", new DateOnly(2015, 1, 1)),
            new Company("beta-1", "u1", "Beta Holdings Pty Ltd", "abn2", new DateOnly(2018, 1, 1)),
        ];

        var result = ToolResultFormatter.FormatCompanyList(companies);

        Assert.Equal("You have 2 companies: Acme Pty Ltd, Beta Holdings Pty Ltd.", result);
    }

    [Fact]
    public void FormatComplianceStatus_Overdue_ListsEachObligationWithDueDate()
    {
        var status = new ComplianceStatusView(
            "beta-1", new DateOnly(2026, 8, 20), "Overdue",
            OverdueObligations: [new Obligation("Annual review lodgement", new DateOnly(2026, 8, 20))],
            UpcomingObligations: []);

        var result = ToolResultFormatter.FormatComplianceStatus("Beta Holdings Pty Ltd", status);

        Assert.Equal(
            "Beta Holdings Pty Ltd's compliance status is Overdue: Annual review lodgement (was due 2026-08-20).",
            result);
    }

    [Fact]
    public void FormatComplianceStatus_Active_MentionsNextReviewDate()
    {
        var status = new ComplianceStatusView(
            "acme-1", new DateOnly(2026, 11, 5), "Active",
            OverdueObligations: [],
            UpcomingObligations: [new Obligation("Annual review lodgement", new DateOnly(2026, 11, 5))]);

        var result = ToolResultFormatter.FormatComplianceStatus("Acme Pty Ltd", status);

        Assert.Equal("Acme Pty Ltd's next annual review is due 2026-11-05 and its status is Active.", result);
    }
}
