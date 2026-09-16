namespace ComplianceCopilot.Shared.Agent;

public enum RouteDecision
{
    RagOnly,
    ToolOnly,
    Both,
    Unclear,
}

/// <summary>
/// The routing decision - RAG only, tool only, both, or neither - is deterministic code, a
/// pure function of an already-parsed Intent. The LLM never decides this directly; it only
/// ever supplies the Intent that this function then acts on.
/// </summary>
public static class QueryRouter
{
    public static RouteDecision Decide(Intent intent)
    {
        var needsTool = intent.ToolIntent != ToolIntentKind.None;

        return (needsTool, intent.NeedsRag) switch
        {
            (true, true) => RouteDecision.Both,
            (true, false) => RouteDecision.ToolOnly,
            (false, true) => RouteDecision.RagOnly,
            (false, false) => RouteDecision.Unclear,
        };
    }
}
