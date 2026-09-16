using System.Text;
using System.Text.RegularExpressions;

namespace ComplianceCopilot.Shared.Rag;

/// <summary>
/// Section-based chunking: splits on markdown headings (## / ### / ...), not fixed
/// character counts. These are real guidance pages with genuine heading structure, so a
/// section is already a coherent unit of meaning - splitting there preserves that, where a
/// blind fixed-size split could cut a sentence (or an obligation) in half. Each chunk keeps
/// its heading text prefixed in, so retrieval sees the section context, not just a stray
/// paragraph.
/// </summary>
public static partial class DocumentChunker
{
    [GeneratedRegex(@"^(#{2,6})\s+(.*)$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    public static IReadOnlyList<RagChunk> ChunkMarkdownBySection(string sourceId, string markdownText)
    {
        var chunks = new List<RagChunk>();
        var matches = HeadingPattern().Matches(markdownText);

        if (matches.Count == 0)
        {
            var trimmed = markdownText.Trim();
            return trimmed.Length == 0
                ? []
                : [new RagChunk($"{sourceId}#1", sourceId, Heading: sourceId, Text: trimmed)];
        }

        var index = 1;
        for (var i = 0; i < matches.Count; i++)
        {
            var heading = matches[i].Groups[2].Value.Trim();
            var bodyStart = matches[i].Index + matches[i].Length;
            var bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : markdownText.Length;
            var body = markdownText[bodyStart..bodyEnd].Trim();

            if (body.Length == 0)
                continue; // a heading with no body before the next heading contributes nothing to retrieve

            var text = new StringBuilder()
                .Append(heading).Append('\n').Append('\n').Append(body)
                .ToString();

            chunks.Add(new RagChunk($"{sourceId}#{index}", sourceId, heading, text));
            index++;
        }

        return chunks;
    }
}
