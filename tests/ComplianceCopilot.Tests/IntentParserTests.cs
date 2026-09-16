using ComplianceCopilot.Shared.Agent;

namespace ComplianceCopilot.Tests;

/// <summary>
/// Fixed sample LLM outputs, no live model needed - this is the deterministic half of the
/// agentic/deterministic boundary, and it's testable precisely because the LLM's output was
/// captured as a plain string first.
/// </summary>
public class IntentParserTests
{
    [Fact]
    public void Parse_CleanJson_ReturnsExpectedIntent()
    {
        const string raw = """
            {"needsGeneralKnowledge": true, "toolIntent": "ComplianceStatus", "companyName": "Acme Pty Ltd", "documentType": null}
            """;

        var intent = IntentParser.Parse(raw);

        Assert.True(intent.NeedsRag);
        Assert.Equal(ToolIntentKind.ComplianceStatus, intent.ToolIntent);
        Assert.Equal("Acme Pty Ltd", intent.CompanyName);
        Assert.Null(intent.DocumentType);
    }

    [Fact]
    public void Parse_JsonWrappedInMarkdownFence_StripsTheFenceAndParses()
    {
        const string raw = """
            Sure, here you go:
            ```json
            {"needsGeneralKnowledge": false, "toolIntent": "ListCompanies", "companyName": null, "documentType": null}
            ```
            """;

        var intent = IntentParser.Parse(raw);

        Assert.False(intent.NeedsRag);
        Assert.Equal(ToolIntentKind.ListCompanies, intent.ToolIntent);
    }

    [Fact]
    public void Parse_JsonWithSurroundingProse_ExtractsOutermostBraces()
    {
        const string raw = """
            The user is asking about a document, so: {"needsGeneralKnowledge": false, "toolIntent": "Document", "companyName": "Beta Holdings Pty Ltd", "documentType": "Constitution"} - that's my answer.
            """;

        var intent = IntentParser.Parse(raw);

        Assert.Equal(ToolIntentKind.Document, intent.ToolIntent);
        Assert.Equal("Beta Holdings Pty Ltd", intent.CompanyName);
        Assert.Equal("Constitution", intent.DocumentType);
    }

    [Fact]
    public void Parse_CompletelyMalformedOutput_FallsBackSafely()
    {
        var intent = IntentParser.Parse("I'm not sure what you mean, could you clarify?");

        Assert.True(intent.NeedsRag);
        Assert.Equal(ToolIntentKind.None, intent.ToolIntent);
        Assert.Null(intent.CompanyName);
    }

    [Fact]
    public void Parse_UnrecognisedEnumValue_FallsBackSafely()
    {
        const string raw = """
            {"needsGeneralKnowledge": true, "toolIntent": "SomethingTheModelMadeUp", "companyName": null, "documentType": null}
            """;

        var intent = IntentParser.Parse(raw);

        Assert.True(intent.NeedsRag);
        Assert.Equal(ToolIntentKind.None, intent.ToolIntent);
    }

    [Fact]
    public void Parse_EmptyString_FallsBackSafely()
    {
        var intent = IntentParser.Parse("");

        Assert.True(intent.NeedsRag);
        Assert.Equal(ToolIntentKind.None, intent.ToolIntent);
    }

    [Fact]
    public void Parse_BlankCompanyNameField_NormalisesToNull()
    {
        const string raw = """
            {"needsGeneralKnowledge": true, "toolIntent": "None", "companyName": "   ", "documentType": ""}
            """;

        var intent = IntentParser.Parse(raw);

        Assert.Null(intent.CompanyName);
        Assert.Null(intent.DocumentType);
    }
}
