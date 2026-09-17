using ComplianceCopilot.Shared.Rag;

namespace ComplianceCopilot.Tests;

public class CitationGroundingCheckerTests
{
    private static readonly Dictionary<string, string> Chunks = new()
    {
        ["asic-annual-review#3"] = "Directors must pass a solvency resolution within 2 months of the annual review date.",
        ["asic-annual-review#5"] = "A late annual company review payment fee will apply if you do not pay by the due date.",
    };

    [Fact]
    public void Check_EveryClaimCitedAndSupported_IsFullyGrounded()
    {
        var answer = "Directors must pass a solvency resolution within 2 months [chunk:asic-annual-review#3].";

        var result = CitationGroundingChecker.Check(answer, Chunks);

        Assert.True(result.IsFullyGrounded);
        Assert.Empty(result.UngroundedSentences);
        Assert.Empty(result.InvalidCitationIds);
        Assert.Empty(result.FabricatedNumbers);
    }

    [Fact]
    public void Check_SentenceWithNoCitation_IsFlaggedUngrounded()
    {
        var answer = "Directors must pass a solvency resolution within 2 months.";

        var result = CitationGroundingChecker.Check(answer, Chunks);

        Assert.False(result.IsFullyGrounded);
        Assert.Single(result.UngroundedSentences);
    }

    [Fact]
    public void Check_CitationToChunkIdNotRetrieved_IsFlaggedInvalid()
    {
        var answer = "Directors must pass a solvency resolution within 2 months [chunk:not-a-real-chunk].";

        var result = CitationGroundingChecker.Check(answer, Chunks);

        Assert.False(result.IsFullyGrounded);
        Assert.Contains("not-a-real-chunk", result.InvalidCitationIds);
    }

    [Fact]
    public void Check_NumberNotPresentInCitedChunkText_IsFlaggedFabricated()
    {
        // The cited chunk never names a dollar figure - this is the brief's own example of a
        // small model inventing a fictional penalty amount and attaching a real citation to it.
        var answer = "A late annual company review payment fee of $273 will apply [chunk:asic-annual-review#5].";

        var result = CitationGroundingChecker.Check(answer, Chunks);

        Assert.False(result.IsFullyGrounded);
        Assert.Single(result.FabricatedNumbers);
        Assert.Contains("$273", result.FabricatedNumbers[0]);
    }

    [Fact]
    public void Check_NumberPresentInCitedChunkText_IsNotFlagged()
    {
        var answer = "Directors must pass a solvency resolution within 2 months [chunk:asic-annual-review#3].";

        var result = CitationGroundingChecker.Check(answer, Chunks);

        Assert.Empty(result.FabricatedNumbers);
    }

    [Fact]
    public void Check_RealCapturedHallucination_QwenOneAndAHalfBFabricatedFeeAmount_IsCaught()
    {
        // Captured live 2026-09-17 querying qwen2.5:1.5b directly (see commit body): asked "what
        // is the exact dollar amount of the late annual review fee", it answered "The late
        // annual review payment fee is $20. [chunk:asic-company-annual-review#5]" - a real
        // citation attached to a figure the source chunk never states (it only says the amount
        // "will be on your annual statement" and varies by company type). qwen2.5:7b-instruct,
        // asked the same question via the same weak prompt, correctly declined instead.
        var chunks = new Dictionary<string, string>
        {
            ["asic-company-annual-review#5"] =
                "You must pay the annual review fee by the due date on the annual statement, which is " +
                "usually 2 months after the annual review date. The fee amount and payment options will " +
                "be on your annual statement. Fee amounts vary by company type (proprietary company, " +
                "special purpose company, or public company). If you do not pay the annual review fee by " +
                "the due date, a late annual company review payment fee will apply. In some cases, ASIC " +
                "may consider deregistering your company.",
        };
        var answer = "The late annual review payment fee is $20. [chunk:asic-company-annual-review#5]";

        var result = CitationGroundingChecker.Check(answer, chunks);

        Assert.False(result.IsFullyGrounded);
        Assert.Single(result.FabricatedNumbers);
        Assert.Contains("$20", result.FabricatedNumbers[0]);
    }

    [Fact]
    public void Check_NumberSupportedByAnyOfMultipleCitationsOnSameSentence_IsNotFlagged()
    {
        var answer = "A late fee applies after 2 months [chunk:asic-annual-review#3][chunk:asic-annual-review#5].";

        var result = CitationGroundingChecker.Check(answer, Chunks);

        Assert.Empty(result.FabricatedNumbers);
    }
}
