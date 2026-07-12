using ObsidianDocsMcp.Eval;
using ObsidianDocsMcp.Models;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public class MetricsTests
{
    private static SearchResult Ranked(string filePath, string header = "General") => new()
    {
        FilePath = filePath,
        Header = header,
        Title = filePath,
        Content = "content"
    };

    private static GoldenQuery Query(params RelevantDoc[] relevant) => new()
    {
        Id = "q-test",
        Query = "test query",
        Relevant = relevant.ToList()
    };

    [Fact]
    public void IsMatch_NormalizesSeparatorsAndCase()
    {
        var expected = new RelevantDoc { FilePath = @"en\Plugins\Vault.md" };

        Assert.True(Metrics.IsMatch(expected, Ranked("en/plugins/vault.md")));
        Assert.False(Metrics.IsMatch(expected, Ranked("en/plugins/other.md")));
    }

    [Fact]
    public void IsMatch_HeaderIsPrefixMatchedToToleratePartSuffixes()
    {
        var expected = new RelevantDoc { FilePath = "en/doc.md", Header = "Configuration" };

        Assert.True(Metrics.IsMatch(expected, Ranked("en/doc.md", "Configuration (Part 2)")));
        Assert.False(Metrics.IsMatch(expected, Ranked("en/doc.md", "Other section")));
    }

    [Fact]
    public void Evaluate_ComputesRecallPrecisionAndMrr()
    {
        // Relevant: A (grade 2), B (grade 1). Ranked: [noise, A, B].
        var query = Query(
            new RelevantDoc { FilePath = "a.md", Grade = 2 },
            new RelevantDoc { FilePath = "b.md", Grade = 1 });
        var ranked = new List<SearchResult> { Ranked("noise.md"), Ranked("a.md"), Ranked("b.md") };

        var metrics = Metrics.Evaluate(query, ranked);

        Assert.Equal(0.0, metrics["recall@1"]);
        Assert.Equal(1.0, metrics["recall@3"]);
        Assert.Equal(2.0 / 3.0, metrics["precision@3"], 6);
        Assert.Equal(0.5, metrics["mrr"]); // first relevant hit at rank 2
    }

    [Fact]
    public void Evaluate_ComputesNdcgWithGradedRelevance()
    {
        var query = Query(
            new RelevantDoc { FilePath = "a.md", Grade = 2 },
            new RelevantDoc { FilePath = "b.md", Grade = 1 });
        var ranked = new List<SearchResult> { Ranked("noise.md"), Ranked("a.md"), Ranked("b.md") };

        var metrics = Metrics.Evaluate(query, ranked);

        // DCG  = (2^2-1)/log2(2+1) + (2^1-1)/log2(3+1) = 3/1.58496 + 1/2      = 2.39279
        // IDCG = (2^2-1)/log2(1+1) + (2^1-1)/log2(2+1) = 3       + 0.63093    = 3.63093
        Assert.Equal(0.65900, metrics["ndcg@10"], 4);
    }

    [Fact]
    public void Evaluate_PerfectRankingScoresOne()
    {
        var query = Query(
            new RelevantDoc { FilePath = "a.md", Grade = 2 },
            new RelevantDoc { FilePath = "b.md", Grade = 1 });
        var ranked = new List<SearchResult> { Ranked("a.md"), Ranked("b.md") };

        var metrics = Metrics.Evaluate(query, ranked);

        Assert.Equal(1.0, metrics["recall@3"]);
        Assert.Equal(1.0, metrics["mrr"]);
        Assert.Equal(1.0, metrics["ndcg@10"], 6);
    }

    [Fact]
    public void Evaluate_NoRelevantResultsScoresZero()
    {
        var query = Query(new RelevantDoc { FilePath = "a.md", Grade = 2 });
        var ranked = new List<SearchResult> { Ranked("noise1.md"), Ranked("noise2.md") };

        var metrics = Metrics.Evaluate(query, ranked);

        Assert.Equal(0.0, metrics["recall@10"]);
        Assert.Equal(0.0, metrics["mrr"]);
        Assert.Equal(0.0, metrics["ndcg@10"]);
    }

    [Fact]
    public void Evaluate_DuplicateChunksOfSameFileCountOnceForRecall()
    {
        var query = Query(new RelevantDoc { FilePath = "a.md", Grade = 2 });
        var ranked = new List<SearchResult> { Ranked("a.md", "Section 1"), Ranked("a.md", "Section 2") };

        var metrics = Metrics.Evaluate(query, ranked);

        Assert.Equal(1.0, metrics["recall@3"]);
        Assert.Equal(1.0, metrics["mrr"]);
    }

    [Fact]
    public void Evaluate_GradeZeroAnnotationsAreIgnored()
    {
        var query = Query(
            new RelevantDoc { FilePath = "a.md", Grade = 2 },
            new RelevantDoc { FilePath = "ignored.md", Grade = 0 });
        var ranked = new List<SearchResult> { Ranked("a.md") };

        var metrics = Metrics.Evaluate(query, ranked);

        Assert.Equal(1.0, metrics["recall@1"]); // ignored.md does not count against recall
    }

    [Fact]
    public void Evaluate_QueryWithoutRelevantDocsThrows()
    {
        var query = Query(new RelevantDoc { FilePath = "a.md", Grade = 0 });

        Assert.Throws<InvalidOperationException>(() => Metrics.Evaluate(query, new List<SearchResult>()));
    }

    [Fact]
    public void Aggregate_AveragesEachMetricAcrossQueries()
    {
        var perQuery = new List<Dictionary<string, double>>
        {
            new() { ["recall@3"] = 1.0, ["mrr"] = 1.0 },
            new() { ["recall@3"] = 0.0, ["mrr"] = 0.5 }
        };

        var aggregated = Metrics.Aggregate(perQuery);

        Assert.Equal(0.5, aggregated["recall@3"]);
        Assert.Equal(0.75, aggregated["mrr"]);
    }
}
