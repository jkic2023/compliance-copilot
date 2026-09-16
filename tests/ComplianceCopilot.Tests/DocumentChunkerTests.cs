using ComplianceCopilot.Shared.Rag;

namespace ComplianceCopilot.Tests;

public class DocumentChunkerTests
{
    [Fact]
    public void ChunkMarkdownBySection_SplitsOnHeadings_OneChunkPerSection()
    {
        const string markdown = """
            # Document Title

            Some intro text under the H1 - not split on since H1 isn't a section boundary here.

            ## Section One

            Body of section one.

            ## Section Two

            Body of section two.
            """;

        var chunks = DocumentChunker.ChunkMarkdownBySection("doc", markdown);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Section One", chunks[0].Heading);
        Assert.Contains("Body of section one.", chunks[0].Text);
        Assert.Equal("Section Two", chunks[1].Heading);
    }

    [Fact]
    public void ChunkMarkdownBySection_AssignsStableCitableIds()
    {
        const string markdown = "## First\n\nbody\n\n## Second\n\nbody";

        var chunks = DocumentChunker.ChunkMarkdownBySection("my-source", markdown);

        Assert.Equal("my-source#1", chunks[0].ChunkId);
        Assert.Equal("my-source#2", chunks[1].ChunkId);
    }

    [Fact]
    public void ChunkMarkdownBySection_SkipsHeadingsWithNoBody()
    {
        const string markdown = "## Empty Heading\n\n## Real Section\n\nActual content here.";

        var chunks = DocumentChunker.ChunkMarkdownBySection("doc", markdown);

        Assert.Single(chunks);
        Assert.Equal("Real Section", chunks[0].Heading);
    }

    [Fact]
    public void ChunkMarkdownBySection_HandlesNestedSubheadings_AsSeparateChunks()
    {
        const string markdown = """
            ## Top Section

            top body

            ### Sub Section

            sub body
            """;

        var chunks = DocumentChunker.ChunkMarkdownBySection("doc", markdown);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Top Section", chunks[0].Heading);
        Assert.Equal("Sub Section", chunks[1].Heading);
    }

    [Fact]
    public void ChunkMarkdownBySection_NoHeadingsAtAll_ReturnsSingleWholeTextChunk()
    {
        var chunks = DocumentChunker.ChunkMarkdownBySection("plain", "Just a paragraph, no headings.");

        Assert.Single(chunks);
        Assert.Equal("plain#1", chunks[0].ChunkId);
    }

    [Fact]
    public void ChunkMarkdownBySection_EmptyInput_ReturnsNoChunks()
    {
        var chunks = DocumentChunker.ChunkMarkdownBySection("empty", "   ");

        Assert.Empty(chunks);
    }
}
