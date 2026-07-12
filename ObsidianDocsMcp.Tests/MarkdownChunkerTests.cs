using ObsidianDocsMcp.Services;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public class MarkdownChunkerTests
{
    private const string Path = "en/Guides/Sample.md";
    private const string Title = "Sample";
    private const string Source = "Developer Docs";

    [Fact]
    public void Segment_SplitsOnHeadingsLevel1To4()
    {
        var markdown = """
            # Intro
            Intro text.
            ## Setup
            Setup text.
            ### Details
            Details text.
            #### Fine print
            Fine print text.
            """;

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        Assert.Equal(4, chunks.Count);
        Assert.Equal(["Intro", "Setup", "Details", "Fine print"], chunks.Select(c => c.Header));
        Assert.All(chunks, c => Assert.Equal($"{Source} > {Title}", c.Title));
        Assert.All(chunks, c => Assert.Equal(Path, c.FilePath));
    }

    [Fact]
    public void Segment_KeepsLevel5PlusHeadingsInsideParentSection()
    {
        var markdown = """
            # Intro
            Intro text.
            ##### Deep heading
            Deep text.
            """;

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Intro", chunk.Header);
        Assert.Contains("Deep heading", chunk.Content);
        Assert.Contains("Deep text.", chunk.Content);
    }

    [Fact]
    public void Segment_IgnoresHeadingsInsideFencedCodeBlocks()
    {
        var markdown = """
            # Real heading
            Some text.
            ```
            # not a heading
            ```
            More text.
            """;

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Real heading", chunk.Header);
        Assert.Contains("# not a heading", chunk.Content);
    }

    [Fact]
    public void Segment_DetectsSetextHeadings()
    {
        var markdown = "My Title\n===\nBody text under a setext heading.";

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        var chunk = Assert.Single(chunks);
        Assert.Equal("My Title", chunk.Header);
        Assert.Contains("Body text", chunk.Content);
    }

    [Fact]
    public void Segment_PutsPreambleBeforeFirstHeadingInGeneralChunk()
    {
        var markdown = """
            Some intro before any heading.

            # First heading
            Section body.
            """;

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("General", chunks[0].Header);
        Assert.Contains("Some intro", chunks[0].Content);
        Assert.Equal("First heading", chunks[1].Header);
    }

    [Fact]
    public void Segment_FileWithoutHeadingsBecomesSingleGeneralChunk()
    {
        var chunks = MarkdownChunker.Segment("Just plain text, no headings.", Path, Title, Source);

        var chunk = Assert.Single(chunks);
        Assert.Equal("General", chunk.Header);
        Assert.Equal("Just plain text, no headings.", chunk.Content);
    }

    [Fact]
    public void Segment_SplitsOversizedSectionsIntoParts()
    {
        var longBody = new string('a', MarkdownChunker.MaxChunkChars + 500);
        var markdown = $"# Big section\n{longBody}";

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Big section (Part 1)", chunks[0].Header);
        Assert.Equal("Big section (Part 2)", chunks[1].Header);
        Assert.Equal(MarkdownChunker.MaxChunkChars, chunks[0].Content.Length);
        Assert.Equal(500, chunks[1].Content.Length);
    }

    [Fact]
    public void Segment_SkipsEmptyAndWhitespaceSections()
    {
        var markdown = """
            # Empty section

            # Full section
            Actual content.
            """;

        var chunks = MarkdownChunker.Segment(markdown, Path, Title, Source);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Full section", chunk.Header);
    }

    [Fact]
    public void Segment_ProducesDeterministicIds()
    {
        var markdown = "# Heading\nContent.";

        var first = MarkdownChunker.Segment(markdown, Path, Title, Source);
        var second = MarkdownChunker.Segment(markdown, Path, Title, Source);

        Assert.Equal(first.Select(c => c.Id), second.Select(c => c.Id));
        Assert.All(first, c => Assert.Matches("^[0-9a-f]{64}$", c.Id));
    }
}
