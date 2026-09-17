using ComplianceCopilot.Shared.Rag;

namespace ComplianceCopilot.Agent.Rag;

/// <summary>Whether the returned answer passed the grounding check, and whether a correction pass was needed.</summary>
public sealed record RagAnswerResult(string Answer, bool IsGrounded, bool RegenerationAttempted);

/// <summary>
/// Owns the full RAG hallucination-mitigation loop: retrieve, generate a draft, run
/// CitationGroundingChecker against it, and - only on failure - ask for exactly one correction
/// pass before abstaining. Two failures in a row means abstain; the model never gets a third
/// try, and a failed correction is never returned to the user disguised as a normal answer.
///
/// Known limitation (see README): a legitimate abstention from the chat model itself (e.g. "I
/// don't have enough information to answer that") has no citation, so it fails check 1 the same
/// way a genuinely unsupported claim would - this triggers one wasted correction attempt before
/// landing on the same abstention message either way. Not a hidden bug: the user-visible outcome
/// is identical, it just costs one extra local-model call.
/// </summary>
public sealed class VerifiedRagAnswerGenerator(RagRetriever retriever, RagAnswerGenerator generator)
{
    private const string AbstentionMessage =
        "I don't have enough information in my knowledge base to answer that confidently.";

    public async Task<RagAnswerResult> AnswerAsync(string query, CancellationToken ct = default)
    {
        var chunks = await retriever.RetrieveAsync(query, ct);
        if (chunks.Count == 0)
        {
            // RagAnswerGenerator itself returns the fixed empty-context message without calling
            // the model at all - nothing to ground-check.
            var emptyAnswer = await generator.GenerateAsync(query, chunks, ct);
            return new RagAnswerResult(emptyAnswer, IsGrounded: true, RegenerationAttempted: false);
        }

        var chunkTexts = chunks.ToDictionary(c => c.Chunk.ChunkId, c => c.Chunk.Text);

        var draft = await generator.GenerateAsync(query, chunks, ct);
        var check = CitationGroundingChecker.Check(draft, chunkTexts);
        if (check.IsFullyGrounded)
            return new RagAnswerResult(draft, IsGrounded: true, RegenerationAttempted: false);

        var corrected = await generator.RegenerateAsync(query, chunks, draft, check, ct);
        var recheck = CitationGroundingChecker.Check(corrected, chunkTexts);
        if (recheck.IsFullyGrounded)
            return new RagAnswerResult(corrected, IsGrounded: true, RegenerationAttempted: true);

        return new RagAnswerResult(AbstentionMessage, IsGrounded: false, RegenerationAttempted: true);
    }
}
