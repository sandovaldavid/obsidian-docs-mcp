using ObsidianDocsMcp.Models;
using ObsidianDocsMcp.Services;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public class RankFusionTests
{
    private static SearchResult Result(string filePath, string header, double matchPercent, string sourceType) => new()
    {
        FilePath = filePath,
        Header = header,
        Title = filePath,
        Content = "content",
        MatchPercent = matchPercent,
        SourceType = sourceType
    };

    [Fact]
    public void ItemPresentInBothListsOutranksSingleListItems()
    {
        var vector = new List<SearchResult>
        {
            Result("a.md", "H", 80, "Semantic"),
            Result("both.md", "H", 70, "Semantic")
        };
        var fts = new List<SearchResult>
        {
            Result("b.md", "H", 60, "Keyword"),
            Result("both.md", "H", 50, "Keyword")
        };

        var merged = RankFusion.ReciprocalRankFusion(vector, fts, 10);

        Assert.Equal("both.md", merged[0].FilePath);
    }

    [Fact]
    public void MatchPercentIsMaxAcrossBothLists()
    {
        var vector = new List<SearchResult> { Result("doc.md", "H", 42.4, "Semantic") };
        var fts = new List<SearchResult> { Result("doc.md", "H", 91.6, "Keyword") };

        var merged = RankFusion.ReciprocalRankFusion(vector, fts, 10);

        var result = Assert.Single(merged);
        Assert.Equal(91.6, result.MatchPercent);
    }

    [Fact]
    public void AllResultsAreMarkedHybrid()
    {
        var vector = new List<SearchResult> { Result("a.md", "H", 80, "Semantic") };
        var fts = new List<SearchResult> { Result("b.md", "H", 60, "Keyword") };

        var merged = RankFusion.ReciprocalRankFusion(vector, fts, 10);

        Assert.All(merged, r => Assert.Equal("Hybrid", r.SourceType));
    }

    [Fact]
    public void SameFileDifferentHeaderAreDistinctResults()
    {
        var vector = new List<SearchResult> { Result("doc.md", "Section A", 80, "Semantic") };
        var fts = new List<SearchResult> { Result("doc.md", "Section B", 60, "Keyword") };

        var merged = RankFusion.ReciprocalRankFusion(vector, fts, 10);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void LimitIsRespected()
    {
        var vector = Enumerable.Range(0, 5).Select(i => Result($"v{i}.md", "H", 80 - i, "Semantic")).ToList();
        var fts = Enumerable.Range(0, 5).Select(i => Result($"f{i}.md", "H", 60 - i, "Keyword")).ToList();

        var merged = RankFusion.ReciprocalRankFusion(vector, fts, 3);

        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void EmptyInputsProduceEmptyOutput()
    {
        var merged = RankFusion.ReciprocalRankFusion(new List<SearchResult>(), new List<SearchResult>(), 5);

        Assert.Empty(merged);
    }
}
