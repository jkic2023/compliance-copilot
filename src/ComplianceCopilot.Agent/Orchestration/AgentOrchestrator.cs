using System.Text;
using ComplianceCopilot.Agent.Mcp;
using ComplianceCopilot.Agent.Rag;
using ComplianceCopilot.Shared.Agent;
using ComplianceCopilot.Shared.Domain;
using ComplianceCopilot.Shared.Rag;
using ComplianceCopilot.Shared.Results;
using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Chat;

namespace ComplianceCopilot.Agent.Orchestration;

/// <summary>
/// A resolved tool-derived fact: its user-facing text, the raw values it was built from (for the
/// compose cross-check), and whether it's a real fact at all. IsAvailable is false for every
/// degraded outcome - MCP unreachable, company not found, a clarification question with nothing
/// to answer yet - so HandleBothAsync can tell "a real fact worth blending with an LLM" apart
/// from "a status message that should just be shown alongside the RAG answer, not woven into it."
/// </summary>
public sealed record ToolFact(string Text, IReadOnlyList<string> KeyValues, bool IsAvailable)
{
    public static ToolFact Available(string text, IReadOnlyList<string>? keyValues = null) =>
        new(text, keyValues ?? [], IsAvailable: true);

    public static ToolFact Unavailable(string text) => new(text, [], IsAvailable: false);
}

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
        try
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Every LLM/embedding call in this pipeline goes through Ollama - a connection
            // failure here (Ollama not running, wrong port) would otherwise surface as a raw
            // unhandled exception and stack trace to the user (confirmed live). This is the one
            // place that failure is caught at the top, since it can originate from intent
            // extraction, RAG retrieval/generation, or composition - degrading per-call
            // everywhere it could occur would mean duplicating this same catch four times over.
            logger.LogError(ex, "Could not reach the Ollama service for query: {Query}", query);
            return "I couldn't reach the AI model service right now - please check it's running and try again.";
        }
    }

    private async Task<string> HandleBothAsync(string query, Intent intent, CancellationToken ct)
    {
        // Sequential, not parallel: the tool fact is cheap and deterministic to fetch, and
        // composing needs both pieces anyway - no real latency win from parallelising here,
        // and sequential is simpler to reason about and to log.
        var toolFact = await ResolveToolFactAsync(intent, ct);
        var ragResult = await ragAnswerGenerator.AnswerAsync(query, ct);

        // The compose LLM is only ever asked to blend two REAL facts. If either half is a
        // status message instead (MCP unreachable, company not found, empty RAG retrieval),
        // there's nothing genuine to blend - handing an error string to "combine and rephrase"
        // is exactly what produced a fabricated citation in live testing (see commit 9's
        // discovery notes). Returning both halves plainly instead means the caller always sees
        // whichever half succeeded plus an honest statement about the half that didn't, per the
        // brief's own failure-handling requirement - never a silent drop, never a full refusal.
        if (!toolFact.IsAvailable || !ragResult.IsAnswerAvailable)
        {
            logger.LogInformation(
                "Mixed query degraded to plain concatenation (tool available: {ToolAvailable}, rag available: {RagAvailable}).",
                toolFact.IsAvailable, ragResult.IsAnswerAvailable);
            return $"{toolFact.Text} {ragResult.Answer}";
        }

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
            return ToolUnavailable("I couldn't reach your company records right now.", companiesOutcome.Error!);

        if (intent.ToolIntent == ToolIntentKind.ListCompanies)
            return ToolFact.Available(ToolResultFormatter.FormatCompanyList(companiesOutcome.Value!));

        var company = CompanyResolver.Resolve(intent.CompanyName, companiesOutcome.Value!);
        if (company is null)
        {
            return ToolFact.Unavailable(intent.CompanyName is null
                ? "Which company are you asking about?"
                : $"I couldn't find a company called \"{intent.CompanyName}\" on your account - could you confirm the exact name?");
        }

        return intent.ToolIntent switch
        {
            ToolIntentKind.ComplianceStatus => await GetComplianceStatusFactAsync(company, ct),
            ToolIntentKind.Document => await GetDocumentFactAsync(company, intent.DocumentType, ct),
            _ => ToolFact.Unavailable("I'm not sure what company information you need."),
        };
    }

    private async Task<ToolFact> GetComplianceStatusFactAsync(Company company, CancellationToken ct)
    {
        var outcome = await mcpClient.CallToolAsync<ComplianceStatusView>(
            "get_company_compliance_status",
            new Dictionary<string, object?> { ["companyId"] = company.CompanyId },
            ct);

        if (!outcome.IsSuccess)
            return ToolUnavailable($"I couldn't get {company.Name}'s compliance status right now.", outcome.Error!);

        var text = ToolResultFormatter.FormatComplianceStatus(company.Name, outcome.Value!);
        return ToolFact.Available(text, ToolResultCrossChecker.ExtractKeyValues(outcome.Value!));
    }

    private async Task<ToolFact> GetDocumentFactAsync(Company company, string? documentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return ToolFact.Unavailable($"What kind of document do you need for {company.Name}?");

        var outcome = await mcpClient.CallToolAsync<CompanyDocument>(
            "get_document",
            new Dictionary<string, object?> { ["companyId"] = company.CompanyId, ["documentType"] = documentType },
            ct);

        if (!outcome.IsSuccess)
            return ToolUnavailable($"I couldn't get that document for {company.Name} right now.", outcome.Error!);

        var text = ToolResultFormatter.FormatDocument(outcome.Value!);
        return ToolFact.Available(text, ToolResultCrossChecker.ExtractKeyValues(outcome.Value!));
    }

    /// <summary>
    /// The user-facing text stays a short, clean sentence - the full error (for an Unavailable
    /// outcome, this can include the MCP subprocess's stderr tail; see McpToolClient) is logged
    /// server-side instead of interpolated into what the user sees, matching the brief's own
    /// example ("I couldn't reach your company records right now" - full stop, no internal detail).
    /// </summary>
    private ToolFact ToolUnavailable(string userMessage, ToolError error)
    {
        logger.LogWarning("Tool call unavailable ({ErrorKind}): {ErrorMessage}", error.Kind, error.Message);
        return ToolFact.Unavailable(userMessage);
    }

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
