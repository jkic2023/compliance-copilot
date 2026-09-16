using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ComplianceCopilot.Shared.Agent;

/// <summary>
/// Deterministically parses the LLM's proposed intent JSON into an <see cref="Intent"/>. This
/// class is the agentic/deterministic boundary made concrete: the LLM proposes a JSON string,
/// and this plain code - no model call, no judgement delegated back to the LLM - decides what
/// it means, including what to do when it's malformed. Pure function of a string in, testable
/// against fixed sample outputs with no live model needed.
/// </summary>
public static partial class IntentParser
{
    [GeneratedRegex(@"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline)]
    private static partial Regex FencedJsonPattern();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Safe fallback when the model's output can't be parsed at all: assume general knowledge
    /// might help and no tool call is needed. Deliberately not a crash and not a guess at
    /// partial JSON - a named, defensible default rather than undefined behaviour.
    /// </summary>
    private static readonly Intent FallbackIntent = new(NeedsRag: true, ToolIntentKind.None, null, null);

    public static Intent Parse(string rawLlmOutput)
    {
        var json = ExtractJson(rawLlmOutput);
        if (json is null)
            return FallbackIntent;

        try
        {
            var dto = JsonSerializer.Deserialize<IntentDto>(json, Options);
            if (dto is null)
                return FallbackIntent;

            return new Intent(
                dto.NeedsGeneralKnowledge,
                dto.ToolIntent,
                string.IsNullOrWhiteSpace(dto.CompanyName) ? null : dto.CompanyName.Trim(),
                string.IsNullOrWhiteSpace(dto.DocumentType) ? null : dto.DocumentType.Trim());
        }
        catch (JsonException)
        {
            return FallbackIntent;
        }
    }

    private static string? ExtractJson(string raw)
    {
        var trimmed = raw.Trim();

        // Models sometimes wrap JSON in a markdown code fence despite being told not to -
        // strip that defensively rather than fail on a technicality.
        var fenceMatch = FencedJsonPattern().Match(trimmed);
        if (fenceMatch.Success)
            return fenceMatch.Groups[1].Value;

        // Or add a sentence of preamble before/after the JSON object - take the outermost braces.
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : null;
    }

    private sealed class IntentDto
    {
        public bool NeedsGeneralKnowledge { get; set; }
        public ToolIntentKind ToolIntent { get; set; }
        public string? CompanyName { get; set; }
        public string? DocumentType { get; set; }
    }
}
