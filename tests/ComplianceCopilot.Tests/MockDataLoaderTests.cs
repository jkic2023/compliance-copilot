using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Tests;

/// <summary>
/// Sanity checks on the fixture JSON itself — catches a typo'd companyId/ownerUserId or a
/// malformed date long before it would surface as a confusing failure in the MCP server or
/// agent layers built on top of it.
/// </summary>
public class MockDataLoaderTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Data", "mock-data.json");

    [Fact]
    public void LoadFromFile_ReturnsExpectedCounts()
    {
        var data = MockDataLoader.LoadFromFile(FixturePath);

        Assert.Equal(2, data.Users.Count);
        Assert.Equal(3, data.Companies.Count);
        Assert.Equal(3, data.ComplianceStatuses.Count);
        Assert.Equal(3, data.Documents.Count);
    }

    [Fact]
    public void LoadFromFile_ScopesCompaniesToCorrectOwners()
    {
        var data = MockDataLoader.LoadFromFile(FixturePath);

        var u1CompanyIds = data.Companies
            .Where(c => c.OwnerUserId == "u1")
            .Select(c => c.CompanyId)
            .ToList();
        var u2CompanyIds = data.Companies
            .Where(c => c.OwnerUserId == "u2")
            .Select(c => c.CompanyId)
            .ToList();

        Assert.Equal(["acme-1", "beta-1"], u1CompanyIds);
        Assert.Equal(["gamma-1"], u2CompanyIds);
    }

    [Fact]
    public void LoadFromFile_BetaHoldingsHasTwoObligations()
    {
        var data = MockDataLoader.LoadFromFile(FixturePath);

        var beta = data.ComplianceStatuses.Single(s => s.CompanyId == "beta-1");

        // Raw data only - no "overdue" judgement is baked into the fixture itself.
        // See ComplianceStatusEvaluatorTests for how overdue-vs-upcoming is computed.
        Assert.Equal(2, beta.Obligations.Count);
    }
}
