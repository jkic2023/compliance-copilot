using System.Text.RegularExpressions;

namespace ComplianceCopilot.Shared.Rag;

/// <summary>
/// The result of a grounding check: which sentences carried no citation at all, which cited a
/// chunk ID that wasn't actually retrieved for this query, and which cited a real chunk but
/// stated a number (a fee, a day count, a percentage) that doesn't actually appear anywhere in
/// that chunk's text - the model reusing a genuine [chunk:ID] marker as cover for a fabricated
/// figure. <see cref="IsFullyGrounded"/> is false if any of the three lists is non-empty.
/// </summary>
public sealed record GroundingResult(
    bool IsFullyGrounded,
    IReadOnlyList<string> UngroundedSentences,
    IReadOnlyList<string> InvalidCitationIds,
    IReadOnlyList<string> FabricatedNumbers);

/// <summary>
/// Deterministic post-check on a RAG-generated draft answer - the "code disposes" half of
/// hallucination mitigation (the LLM only ever proposes citations; this class is what actually
/// verifies them before anything reaches the user). Three checks, each targeting a distinct
/// failure mode:
///
/// 1. Every factual sentence must carry a [chunk:ID] marker - a sentence with none is
///    unsupported by construction.
/// 2. Every cited ID must be one of the chunks genuinely retrieved for this query - a citation
///    to an ID the model invented, or one from a different query's context, is caught here.
/// 3. Every number in a cited sentence (a fee amount, a day count, a percentage) must actually
///    appear in the text of the chunk it's cited against - this is the specific case the brief
///    calls out (a fictional penalty amount not in the corpus): a real [chunk:ID] marker
///    attached to a fabricated figure would pass check 1 and 2 but fails this one.
/// </summary>
public static class CitationGroundingChecker
{
    private static readonly Regex CitationPattern = new(@"\[chunk:([^\]]+)\]", RegexOptions.Compiled);

    // Naive sentence splitter - breaks on '.', '!', '?' followed by whitespace, unless what
    // follows is a citation marker (observed live: qwen2.5:1.5b routinely writes "...fee is $20.
    // [chunk:5]" with the marker after the full stop, not before it - without this guard that
    // splits into an ungrounded claim sentence plus a citation-only fragment, silently missing
    // the fabricated-number check entirely even though the sentence is still correctly flagged
    // as ungrounded). Known remaining limitation (see README): a decimal number or abbreviation
    // inside a sentence can still mis-split.
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+(?!\[chunk:)", RegexOptions.Compiled);

    // Matches a dollar amount, a plain number, or a percentage - the categories of figure a
    // small model is prone to fabricate when the source material only says "the fee amount
    // will be on your statement" rather than naming one.
    private static readonly Regex NumberToken = new(@"\$?\d[\d,]*(?:\.\d+)?%?", RegexOptions.Compiled);

    /// <summary>
    /// The distinct [chunk:ID] markers present in a piece of text, with no validation against
    /// what was actually retrieved - used by AgentOrchestrator.ComposeAsync to detect when the
    /// compose step has silently dropped a citation that survived RagAnswerGenerator's own
    /// grounding check (observed live: the compose model sometimes drops citations and drifts
    /// toward unsupported filler when asked to blend a tool fact with a RAG answer).
    /// </summary>
    public static IReadOnlySet<string> ExtractCitedIds(string text) =>
        CitationPattern.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    public static GroundingResult Check(string answer, IReadOnlyDictionary<string, string> retrievedChunkTexts)
    {
        var sentences = SentenceSplit
            .Split(answer.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var ungrounded = new List<string>();
        var invalidIds = new List<string>();
        var fabricatedNumbers = new List<string>();

        foreach (var sentence in sentences)
        {
            var citations = CitationPattern.Matches(sentence);
            if (citations.Count == 0)
            {
                ungrounded.Add(sentence);
                continue;
            }

            var citedTexts = new List<string>();
            foreach (Match citation in citations)
            {
                var id = citation.Groups[1].Value;
                if (retrievedChunkTexts.TryGetValue(id, out var text))
                    citedTexts.Add(text);
                else
                    invalidIds.Add(id);
            }

            if (citedTexts.Count == 0)
                continue; // every citation on this sentence was invalid - already recorded above

            // Scan for numbers in the claim text only - not inside the citation markers
            // themselves, whose chunk IDs (e.g. "asic-annual-review#3") routinely contain
            // digits that have nothing to do with the sentence's actual claim.
            var claimText = CitationPattern.Replace(sentence, "");
            foreach (Match numberMatch in NumberToken.Matches(claimText))
            {
                var number = numberMatch.Value;
                if (!citedTexts.Any(text => text.Contains(number, StringComparison.Ordinal)))
                    fabricatedNumbers.Add($"\"{number}\" in: {sentence}");
            }
        }

        return new GroundingResult(
            IsFullyGrounded: ungrounded.Count == 0 && invalidIds.Count == 0 && fabricatedNumbers.Count == 0,
            ungrounded,
            invalidIds,
            fabricatedNumbers);
    }
}
