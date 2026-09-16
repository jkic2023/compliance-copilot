using System.Text;
using ComplianceCopilot.Agent.Mcp;
using ComplianceCopilot.Agent.Rag;
using ComplianceCopilot.Shared.Agent;
using ComplianceCopilot.Shared.Domain;
using OllamaSharp.Models.Chat;

namespace ComplianceCopilot.Agent.Orchestration;

/// <summary>
/// Per-turn control flow. Everything about WHICH path is taken (route, company resolution,
/// tool arguments, whether to call the LLM at all) is decided by deterministic code below -
/// the LLM is only ever asked to (a) propose an Intent, (b) draft a RAG answer over already-
/// retrieved chunks, or (c) rephrase two already-produced facts together. It never decides
/// what happens next in the pipeline.
/// </summary>
public sealed class AgentOrchestrator(
    IntentExtractor intentExtractor,
    RagRetriever ragRetriever,
    RagAnswerGenerator ragAnswerGenerator,
    McpToolClient mcpClient,
    OllamaClients ollama)
{
    private const string ComposeSystemPrompt = """
        Combine the two pieces of information below into a single, natural-sounding answer to
        the user's question. Do not add any new facts beyond what's given - only combine,
        rephrase, and connect what's already here. Keep any [chunk:ID] citations exactly as
        written, character for character.
        """;

    public async Task<string> HandleAsync(string query, CancellationToken ct = default)
    {
        var intent = await intentExtractor.ExtractAsync(query, ct);
        var route = QueryRouter.Decide(intent);

        return route switch
        {
            RouteDecision.RagOnly => await HandleRagOnlyAsync(query, ct),
            RouteDecision.ToolOnly => await ResolveToolFactAsync(intent, ct),
            RouteDecision.Both => await HandleBothAsync(query, intent, ct),
            _ => "I'm not sure what you're asking - could you mention a specific company, or " +
                 "ask a general compliance question?",
        };
    }

    private async Task<string> HandleRagOnlyAsync(string query, CancellationToken ct)
    {
        var chunks = await ragRetriever.RetrieveAsync(query, ct);
        return await ragAnswerGenerator.GenerateAsync(query, chunks, ct);
    }

    private async Task<string> HandleBothAsync(string query, Intent intent, CancellationToken ct)
    {
        // Sequential, not parallel: the tool fact is cheap and deterministic to fetch, and
        // composing needs both pieces anyway - no real latency win from parallelising here,
        // and sequential is simpler to reason about and to log.
        var toolFact = await ResolveToolFactAsync(intent, ct);
        var chunks = await ragRetriever.RetrieveAsync(query, ct);
        var ragAnswer = await ragAnswerGenerator.GenerateAsync(query, chunks, ct);

        return await ComposeAsync(query, toolFact, ragAnswer, ct);
    }

    private async Task<string> ResolveToolFactAsync(Intent intent, CancellationToken ct)
    {
        // get_user_companies is always called first, deterministically - both to resolve the
        // LLM's free-text company name into a real companyId, and as the answer itself for a
        // ListCompanies intent. The LLM never sees or invents a companyId.
        var companiesOutcome = await mcpClient.CallToolAsync<IReadOnlyList<Company>>(
            "get_user_companies", [], ct);

        if (!companiesOutcome.IsSuccess)
            return $"I couldn't reach your company records right now ({companiesOutcome.Error!.Message}).";

        if (intent.ToolIntent == ToolIntentKind.ListCompanies)
            return ToolResultFormatter.FormatCompanyList(companiesOutcome.Value!);

        var company = CompanyResolver.Resolve(intent.CompanyName, companiesOutcome.Value!);
        if (company is null)
        {
            return intent.CompanyName is null
                ? "Which company are you asking about?"
                : $"I couldn't find a company called \"{intent.CompanyName}\" on your account - could you confirm the exact name?";
        }

        return intent.ToolIntent switch
        {
            ToolIntentKind.ComplianceStatus => await GetComplianceStatusFactAsync(company, ct),
            ToolIntentKind.Document => await GetDocumentFactAsync(company, intent.DocumentType, ct),
            _ => "I'm not sure what company information you need.",
        };
    }

    private async Task<string> GetComplianceStatusFactAsync(Company company, CancellationToken ct)
    {
        var outcome = await mcpClient.CallToolAsync<ComplianceStatusView>(
            "get_company_compliance_status",
            new Dictionary<string, object?> { ["companyId"] = company.CompanyId },
            ct);

        return outcome.IsSuccess
            ? ToolResultFormatter.FormatComplianceStatus(company.Name, outcome.Value!)
            : $"I couldn't get {company.Name}'s compliance status right now ({outcome.Error!.Message}).";
    }

    private async Task<string> GetDocumentFactAsync(Company company, string? documentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return $"What kind of document do you need for {company.Name}?";

        var outcome = await mcpClient.CallToolAsync<CompanyDocument>(
            "get_document",
            new Dictionary<string, object?> { ["companyId"] = company.CompanyId, ["documentType"] = documentType },
            ct);

        return outcome.IsSuccess
            ? ToolResultFormatter.FormatDocument(outcome.Value!)
            : $"I couldn't get that document for {company.Name} right now ({outcome.Error!.Message}).";
    }

    private async Task<string> ComposeAsync(string query, string toolFact, string ragAnswer, CancellationToken ct)
    {
        var userPrompt = $"""
            Question: {query}

            Company-specific fact: {toolFact}

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

        return sb.ToString();
    }
}
