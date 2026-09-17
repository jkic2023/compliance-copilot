using System.Text;
using ComplianceCopilot.Shared.Rag;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace ComplianceCopilot.Agent.Rag;

/// <summary>
/// Generates a draft answer over retrieved chunks, with an inline [chunk:ID] citation required
/// after every factual claim - the "LLM proposes" half of hallucination mitigation. Whether
/// those citations are honest is verified afterwards, deterministically, by
/// CitationGroundingChecker; a failed check routes back to <see cref="RegenerateAsync"/> via
/// VerifiedRagAnswerGenerator, which owns that orchestration.
/// </summary>
public sealed class RagAnswerGenerator(OllamaClients ollama)
{
    private const string SystemPrompt = """
        You are a compliance assistant answering questions about company registration and
        statutory obligations. Answer the user's question using ONLY the information in the
        numbered context chunks provided below - do not use any outside knowledge.

        For every factual claim you make, add a citation marker immediately after it in the
        exact form [chunk:ID], using the ID shown before the chunk you drew that claim from.
        A sentence with no citation marker will be treated as unsupported. Never state a specific
        number (a fee amount, a day count, a percentage) unless that exact number appears in the
        chunk you're citing - if the source only says an amount "will be on your statement"
        without naming one, say that instead of inventing a figure.

        If the context chunks do not contain enough information to answer the question, say so
        plainly instead of guessing.
        """;

    private const string CorrectionSystemPrompt = """
        You are correcting a previous draft answer that failed an automated fact-check. You will
        be given the same context chunks, the question, the previous draft, and a list of
        specific problems found in it. Rewrite the answer so none of those problems remain:

        - A sentence with no [chunk:ID] citation: either add a correct one, or remove the claim.
        - A citation to a chunk ID that was not actually provided below: remove or fix it - never
          invent a chunk ID.
        - A number that doesn't appear in the chunk it's cited against: remove the fabricated
          number, or say the source doesn't specify an exact figure.

        Keep every part of the previous draft that was NOT flagged as a problem. Do not introduce
        any new claims beyond what's in the context chunks.
        """;

    public async Task<string> GenerateAsync(
        string query,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken ct = default)
    {
        if (chunks.Count == 0)
        {
            return "I don't have enough information in my knowledge base to answer that.";
        }

        var contextBlock = string.Join(
            "\n\n",
            chunks.Select(r => $"[chunk:{r.Chunk.ChunkId}]\n{r.Chunk.Text}"));

        var userPrompt = $"Context:\n\n{contextBlock}\n\nQuestion: {query}";

        var request = new ChatRequest
        {
            Model = ollama.Chat.SelectedModel,
            Messages =
            [
                new Message(ChatRole.System, SystemPrompt),
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

    /// <summary>
    /// The one allowed correction attempt after a failed grounding check. Deliberately run on
    /// ollama.Verifier (qwen2.5:1.5b), not the same chat model that produced the flawed draft -
    /// see OllamaClients for why a distinct, smaller model makes this a real second opinion
    /// rather than the same model re-reading its own answer.
    /// </summary>
    public async Task<string> RegenerateAsync(
        string query,
        IReadOnlyList<RetrievedChunk> chunks,
        string previousAnswer,
        GroundingResult failure,
        CancellationToken ct = default)
    {
        var contextBlock = string.Join(
            "\n\n",
            chunks.Select(r => $"[chunk:{r.Chunk.ChunkId}]\n{r.Chunk.Text}"));

        var problems = new List<string>();
        if (failure.UngroundedSentences.Count > 0)
            problems.Add("No citation: " + string.Join(" | ", failure.UngroundedSentences));
        if (failure.InvalidCitationIds.Count > 0)
            problems.Add("Cited a chunk ID that wasn't provided: " + string.Join(", ", failure.InvalidCitationIds));
        if (failure.FabricatedNumbers.Count > 0)
            problems.Add("Number not found in the cited chunk: " + string.Join(" | ", failure.FabricatedNumbers));

        var userPrompt = $"""
            Context:

            {contextBlock}

            Question: {query}

            Previous draft:
            {previousAnswer}

            Problems found:
            {string.Join("\n", problems)}
            """;

        var request = new ChatRequest
        {
            Model = ollama.Verifier.SelectedModel,
            Messages =
            [
                new Message(ChatRole.System, CorrectionSystemPrompt),
                new Message(ChatRole.User, userPrompt),
            ],
            Stream = false,
        };

        var sb = new StringBuilder();
        await foreach (var response in ollama.Verifier.ChatAsync(request, ct))
        {
            if (response?.Message?.Content is { } content)
                sb.Append(content);
        }

        return sb.ToString();
    }
}
