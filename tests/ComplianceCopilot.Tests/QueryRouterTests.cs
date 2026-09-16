using ComplianceCopilot.Shared.Agent;

namespace ComplianceCopilot.Tests;

public class QueryRouterTests
{
    [Fact]
    public void Decide_ToolAndRagBothNeeded_ReturnsBoth()
    {
        var intent = new Intent(NeedsRag: true, ToolIntentKind.ComplianceStatus, "Acme Pty Ltd", null);

        Assert.Equal(RouteDecision.Both, QueryRouter.Decide(intent));
    }

    [Fact]
    public void Decide_OnlyToolNeeded_ReturnsToolOnly()
    {
        var intent = new Intent(NeedsRag: false, ToolIntentKind.ListCompanies, null, null);

        Assert.Equal(RouteDecision.ToolOnly, QueryRouter.Decide(intent));
    }

    [Fact]
    public void Decide_OnlyRagNeeded_ReturnsRagOnly()
    {
        var intent = new Intent(NeedsRag: true, ToolIntentKind.None, null, null);

        Assert.Equal(RouteDecision.RagOnly, QueryRouter.Decide(intent));
    }

    [Fact]
    public void Decide_NeitherNeeded_ReturnsUnclear()
    {
        var intent = new Intent(NeedsRag: false, ToolIntentKind.None, null, null);

        Assert.Equal(RouteDecision.Unclear, QueryRouter.Decide(intent));
    }
}
