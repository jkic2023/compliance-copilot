using ComplianceCopilot.Shared.Domain;

namespace ComplianceCopilot.Tests;

public class CompanyResolverTests
{
    private static readonly IReadOnlyList<Company> Companies =
    [
        new Company("acme-1", "u1", "Acme Pty Ltd", "51 824 753 556", new DateOnly(2015, 3, 12)),
        new Company("beta-1", "u1", "Beta Holdings Pty Ltd", "88 010 452 336", new DateOnly(2018, 7, 1)),
    ];

    [Fact]
    public void Resolve_ExactNameMatch_ReturnsCompany()
    {
        var result = CompanyResolver.Resolve("Acme Pty Ltd", Companies);

        Assert.Equal("acme-1", result?.CompanyId);
    }

    [Fact]
    public void Resolve_CaseInsensitiveExactMatch_ReturnsCompany()
    {
        var result = CompanyResolver.Resolve("acme pty ltd", Companies);

        Assert.Equal("acme-1", result?.CompanyId);
    }

    [Fact]
    public void Resolve_UnambiguousPartialMatch_ReturnsCompany()
    {
        var result = CompanyResolver.Resolve("Acme", Companies);

        Assert.Equal("acme-1", result?.CompanyId);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNullRatherThanGuessing()
    {
        var result = CompanyResolver.Resolve("Gamma Trading Pty Ltd", Companies);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_AmbiguousPartialMatch_ReturnsNullRatherThanGuessing()
    {
        var ambiguous = new List<Company>(Companies)
        {
            new("acme-2", "u1", "Acme Holdings Pty Ltd", "11 111 111 111", new DateOnly(2020, 1, 1)),
        };

        var result = CompanyResolver.Resolve("Acme", ambiguous);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_NullOrEmptyName_ReturnsNull()
    {
        Assert.Null(CompanyResolver.Resolve(null, Companies));
        Assert.Null(CompanyResolver.Resolve("   ", Companies));
    }
}
