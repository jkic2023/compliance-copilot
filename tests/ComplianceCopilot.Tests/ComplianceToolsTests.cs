using ComplianceCopilot.McpServer.Tools;
using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Domain;
using ComplianceCopilot.Shared.Results;
using Microsoft.Extensions.Options;

namespace ComplianceCopilot.Tests;

/// <summary>
/// Tool logic tested directly, without a live MCP protocol connection or DI container -
/// the DI-resolved parameters (MockDataSet, IOptions, TimeProvider) are just passed in by
/// hand. The scoping test here is the one the assessment brief explicitly calls out:
/// a request for a company owned by a different mock user must fail Forbidden, not
/// silently return empty or NotFound.
/// </summary>
public class ComplianceToolsTests
{
    private static readonly MockDataSet Data = new(
        Users:
        [
            new User("u1", "Jordan Lee", "Lee & Associates Bookkeeping"),
            new User("u2", "Priya Nair", "Nair Compliance Services"),
        ],
        Companies:
        [
            new Company("acme-1", "u1", "Acme Pty Ltd", "51 824 753 556", new DateOnly(2015, 3, 12)),
            new Company("beta-1", "u1", "Beta Holdings Pty Ltd", "88 010 452 336", new DateOnly(2018, 7, 1)),
            new Company("gamma-1", "u2", "Gamma Trading Pty Ltd", "23 004 085 616", new DateOnly(2020, 1, 22)),
        ],
        ComplianceStatuses:
        [
            new ComplianceStatus("acme-1", new DateOnly(2026, 11, 5),
                [new Obligation("Annual review lodgement", new DateOnly(2026, 11, 5))]),
            new ComplianceStatus("beta-1", new DateOnly(2026, 8, 20),
                [
                    new Obligation("Annual review lodgement", new DateOnly(2026, 8, 20)),
                    new Obligation("ASIC annual review fee payment", new DateOnly(2026, 8, 20)),
                ]),
            new ComplianceStatus("gamma-1", new DateOnly(2027, 1, 22),
                [new Obligation("Annual review lodgement", new DateOnly(2027, 1, 22))]),
        ],
        Documents:
        [
            new CompanyDocument("acme-1", "Constitution", new DateOnly(2015, 3, 12), "Standard constitution."),
        ]);

    private static IOptions<ComplianceMcpOptions> OptionsFor(string currentUserId) =>
        Options.Create(new ComplianceMcpOptions { CurrentUserId = currentUserId, DataFilePath = "unused" });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider FixedTime =
        new FixedTimeProvider(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetUserCompanies_ReturnsOnlyCurrentUsersCompanies()
    {
        var result = await ComplianceTools.GetUserCompanies(Data, OptionsFor("u1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["acme-1", "beta-1"], result.Value!.Select(c => c.CompanyId));
    }

    [Fact]
    public async Task GetCompanyComplianceStatus_OwnedCompany_ReturnsComputedOverdueStatus()
    {
        var result = await ComplianceTools.GetCompanyComplianceStatus("beta-1", Data, OptionsFor("u1"), FixedTime);

        Assert.True(result.IsSuccess);
        Assert.Equal("Overdue", result.Value!.Label);
        Assert.Equal(2, result.Value.OverdueObligations.Count);
    }

    [Fact]
    public async Task GetCompanyComplianceStatus_CompanyOwnedByAnotherUser_ReturnsForbidden()
    {
        // THE scoping test: u1 requesting u2's company (gamma-1) must fail Forbidden -
        // not NotFound, and not a silently empty/successful result.
        var result = await ComplianceTools.GetCompanyComplianceStatus("gamma-1", Data, OptionsFor("u1"), FixedTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(ToolErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task GetCompanyComplianceStatus_UnknownCompanyId_ReturnsNotFound()
    {
        var result = await ComplianceTools.GetCompanyComplianceStatus("does-not-exist", Data, OptionsFor("u1"), FixedTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(ToolErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task GetCompanyComplianceStatus_EmptyCompanyId_ReturnsValidationFailed()
    {
        var result = await ComplianceTools.GetCompanyComplianceStatus("   ", Data, OptionsFor("u1"), FixedTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(ToolErrorKind.ValidationFailed, result.Error!.Kind);
    }

    [Fact]
    public async Task GetDocument_OwnedCompanyAndKnownType_ReturnsSuccessCaseInsensitively()
    {
        var result = await ComplianceTools.GetDocument("acme-1", "constitution", Data, OptionsFor("u1"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Constitution", result.Value!.DocumentType);
    }

    [Fact]
    public async Task GetDocument_CompanyOwnedByAnotherUser_ReturnsForbidden()
    {
        var result = await ComplianceTools.GetDocument("gamma-1", "Constitution", Data, OptionsFor("u1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ToolErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task GetDocument_UnknownDocumentType_ReturnsNotFound()
    {
        var result = await ComplianceTools.GetDocument("acme-1", "NotARealType", Data, OptionsFor("u1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ToolErrorKind.NotFound, result.Error!.Kind);
    }
}
