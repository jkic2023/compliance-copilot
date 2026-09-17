using System.Text;
using ComplianceCopilot.Agent.Mcp;
using ComplianceCopilot.Agent.Rag;
using ComplianceCopilot.Shared.Agent;
using ComplianceCopilot.Shared.Domain;
using ComplianceCopilot.Shared.Rag;
using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Chat;

namespace ComplianceCopilot.Agent.Orchestration;

/// <summary>A resolved tool-derived fact: its user-facing text, and the raw values it was built from (for the compose cross-check).</summary>
public sealed record ToolFact(string Text, IReadOnlyList<string> KeyValues);

/// <summary>
/// Per-turn control flow. Everything about WHICH path is taken (route, company resolution,
/// tool arguments, whether to call the LLM at all) is decided by deterministic code below -
/// the LLM is only ever asked to (a) propose an Intent, (b) draft a RAG answer over already-
/// retrieved chunks (verified before it comes back here - see VerifiedRagAnswerGenerator), or
/// (c) rephrase two already-produced facts together. It never decides what happens next in the
/// pipeline, and its composed output doesn't reach the user unchecked either - see ComposeAsync.
/// </summary>
public sealed class AgentOrchestrator(
    IntentExtractor intentExtractor,
    VerifiedRagAnswerGenerator ragAnswerGenerator,
    McpToolClient mcpClient,
    OllamaClients ollama,
    ILogger<AgentOrchestrator> logger)
{
    private const string ComposeSystemPrompt = """
        Combine the two pieces of information below into a single, natural-sounding answer to
        the user's question. Do not add any new facts beyond what's given - only combine,
        rephrase, and connect what's already here. Keep any [chunk:ID] citations, dates, and
        numbers exactly as written, character for character - do not reformat or paraphrase them.
        """;

    public async Task<string> HandleAsync(string query, CancellationToken ct = default)
    {
        var intent = await intentExtractor.ExtractAsync(query, ct);
        var route = QueryRouter.Decide(intent);

        return route switch
        {
            RouteDecision.RagOnly => (await ragAnswerGenerator.AnswerAsync(query, ct)).Answer,
            RouteDecision.ToolOnly => (await ResolveToolFactAsync(intent, ct)).Text,
            RouteDecision.Both => await HandleBothAsync(query, intent, ct),
            _ => "I'm not sure what you're asking - could you mention a specific company, or " +
                 "ask a general compliance question?",
        };
    }

    private async Task<string> HandleBothAsync(string query, Intent intent, CancellationToken ct)
    {
        // Sequential, not parallel: the tool fact is cheap and deterministic to fetch, and
        // composing needs both pieces anyway - no real latency win from parallelising here,
        // and sequential is simpler to reason about and to log.
        var toolFact = await ResolveToolFactAsync(intent, ct);
        var ragResult = await ragAnswerGenerator.AnswerAsync(query, ct);

        return await ComposeAsync(query, toolFact, ragResult.Answer, ct);
    }

    private async Task<ToolFact> ResolveToolFactAsync(Intent intent, CancellationToken ct)
    {
        // get_user_companies is always called first, deterministically - both to resolve the
        // LLM's free-text company name into a real companyId, and as the answer itself for a
        // ListCompanies intent. The LLM never sees or invents a companyId.
        var companiesOutcome = await mcpClient.CallToolAsync<IReadOnlyList<Company>>(
            "get_user_companies", [], ct);

        if (!companiesOutcome.IsSuccess)
            return NoKeyValues($"I couldn't reach your company records right now ({companiesOutcome.Error!.Message}).");

        if (intent.ToolIntent == ToolIntentKind.ListCompanies)
            return NoKeyValues(ToolResultFormatter.FormatCompanyList(companiesOutcome.Value!));

        var company = CompanyResolver.Resolve(intent.CompanyName, companiesOutcome.Value!);
        if (company is null)
        {
            return NoKeyValues(intent.CompanyName is null
                ? "Which company are you asking about?"
                : $"I couldn't find a company called \"{intent.CompanyName}\" on your account - could you confirm the exact name?");
        }

        return intent.ToolIntent switch
        {
            ToolIntentKind.ComplianceStatus => await GetComplianceStatusFactAsync(company, ct),
            ToolIntentKind.Document => await GetDocumentFactAsync(company, intent.DocumentType, ct),
            _ => NoKeyValues("I'm not sure what company information you need."),
        };
    }

    private async Task<ToolFact> GetComplianceStatusFactAsync(Company company, CancellationToken ct)
    {
        var outcome = await mcpClient.CallToolAsync<ComplianceStatusView>(
            "get_company_compliance_status",
            new Dictionary<string, object?> { ["companyId"] = company.CompanyId },
            ct);

        if (!outcome.IsSuccess)
            return NoKeyValues($"I couldn't get {company.Name}'s compliance status right now ({outcome.Error!.Message}).");

        var text = ToolResultFormatter.FormatComplianceStatus(company.Name, outcome.Value!);
        return new ToolFact(text, ToolResultCrossChecker.ExtractKeyValues(outcome.Value!));
    }

    private async Task<ToolFact> GetDocumentFactAsync(Company company, string? documentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return NoKeyValues($"What kind of document do you need for {company.Name}?");

        var outcome = await mcpClient.CallToolAsync<CompanyDocument>(
            "get_document",
            new Dictionary<string, object?> { ["companyId"] = company.CompanyId, ["documentType"] = documentType },
            ct);

        if (!outcome.IsSuccess)
            return NoKeyValues($"I couldn't get that document for {company.Name} right now ({outcome.Error!.Message}).");

        var text = ToolResultFormatter.FormatDocument(outcome.Value!);
        return new ToolFact(text, ToolResultCrossChecker.ExtractKeyValues(outcome.Value!));
    }

    private static ToolFact NoKeyValues(string text) => new(text, []);

    /// <summary>
    /// Composes the tool fact and RAG answer into one response, then runs two deterministic
    /// checks against it: the tool-result cross-check (did a date/number drift), and a citation-
    /// preservation check (did the composed text drop a [chunk:ID] the RAG answer had, or invent
    /// one that was never in it - both observed live: the compose model sometimes drops citations
    /// and drifts toward unsupported filler, and separately has invented an entirely fake
    /// [chunk:ID] not among the retrieved chunks, when asked to blend a tool fact with a RAG
    /// answer). Either failure discards the
    /// LLM's phrasing entirely in favour of a plain, safe concatenation of the two
    /// already-verified pieces - not a second LLM attempt, since this class of bug is exactly
    /// the kind of mistake asking the same step to "try again" tends to repeat. The RAG answer's
    /// own citations were already grounding-checked upstream, so the fallback carries no
    /// additional hallucination risk.
    /// </summary>
    private async Task<string> ComposeAsync(string query, ToolFact toolFact, string ragAnswer, CancellationToken ct)
    {
        var userPrompt = $"""
            Question: {query}

            Company-specific fact: {toolFact.Text}

            General knowledge answer: {ragAnswer}
            """;

        var request = new ChatRequest
        {
            Model = ollama.Chat.SelectedModel,
            Messages =
            [
                new Message(ChatRole.System, ComposeSystemPrompt),
                new Message(ChatRole.User, userPrompt),
            ],
            Stream = false,
        };

        var sb = new StringBuilder();
        await foreach (var response in ollama.Chat.ChatAsync(request, ct))
        {
            if (response?.Message?.Content is { } content)
                sb.Append(content);
        }

        var composed = sb.ToString();
        var crossCheck = ToolResultCrossChecker.Check(composed, toolFact.KeyValues);
        var ragCitationIds = CitationGroundingChecker.ExtractCitedIds(ragAnswer);
        var composedCitationIds = CitationGroundingChecker.ExtractCitedIds(composed);
        var droppedCitations = ragCitationIds.Except(composedCitationIds).ToList();
        var inventedCitations = composedCitationIds.Except(ragCitationIds).ToList();

        if (crossCheck.IsConsistent && droppedCitations.Count == 0 && inventedCitations.Count == 0)
            return composed;

        logger.LogWarning(
            "Composed answer failed verification (drifted tool values: [{DriftedValues}]; " +
            "dropped citations: [{DroppedCitations}]; invented citations: [{InventedCitations}]) - " +
            "falling back to plain concatenation.",
            string.Join(", ", crossCheck.DriftedValues),
            string.Join(", ", droppedCitations),
            string.Join(", ", inventedCitations));

        return $"{toolFact.Text} {ragAnswer}";
    }
}
