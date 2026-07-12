using Microsoft.Extensions.Logging.Abstractions;
using ObsidianDocsMcp.Models;
using ObsidianDocsMcp.Services;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public class SearchServiceTests
{
    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public float[]? Vector { get; init; }
        public bool Throws { get; init; }

        public Task<float[]> GetEmbeddingAsync(string text) =>
            Throws ? throw new InvalidOperationException("Ollama is down") : Task.FromResult(Vector ?? []);
    }

    private sealed class FakeDatabaseService : IDatabaseService
    {
        public List<SearchResult> FtsResults { get; init; } = new();
        public List<SearchResult> VectorResults { get; init; } = new();
        public int? FtsLimitSeen { get; private set; }
        public bool VectorSearchCalled { get; private set; }

        public string DbPath => ":memory:";
        public Task InitializeDatabaseAsync() => Task.CompletedTask;
        public Task SaveChunksAsync(List<SectionChunk> chunks) => Task.CompletedTask;
        public Task<int> GetTotalChunksCountAsync() => Task.FromResult(FtsResults.Count + VectorResults.Count);

        public Task<List<SearchResult>> FtsSearchAsync(string queryText, int limit)
        {
            FtsLimitSeen = limit;
            return Task.FromResult(FtsResults);
        }

        public Task<List<SearchResult>> VectorSearchAsync(float[] queryVector, int limit)
        {
            VectorSearchCalled = true;
            return Task.FromResult(VectorResults);
        }
    }

    private static SearchResult Result(string filePath, string sourceType) => new()
    {
        FilePath = filePath,
        Header = "General",
        Title = filePath,
        Content = "content",
        MatchPercent = 50,
        SourceType = sourceType
    };

    private static SearchService CreateService(FakeDatabaseService db, FakeEmbeddingService embedding) =>
        new(db, embedding, NullLogger<SearchService>.Instance);

    [Fact]
    public async Task MergesVectorAndKeywordResultsIntoHybridRanking()
    {
        var db = new FakeDatabaseService
        {
            FtsResults = [Result("keyword.md", "Keyword"), Result("both.md", "Keyword")],
            VectorResults = [Result("semantic.md", "Semantic"), Result("both.md", "Semantic")]
        };
        var service = CreateService(db, new FakeEmbeddingService { Vector = [1f, 0f] });

        var results = await service.SearchAsync("query", 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("both.md", results[0].FilePath); // present in both lists → best RRF score
        Assert.All(results, r => Assert.Equal("Hybrid", r.SourceType));
    }

    [Fact]
    public async Task OverfetchesCandidatesAtTwiceTheRequestedLimit()
    {
        var db = new FakeDatabaseService();
        var service = CreateService(db, new FakeEmbeddingService { Vector = [1f, 0f] });

        await service.SearchAsync("query", 3);

        Assert.Equal(6, db.FtsLimitSeen);
    }

    [Fact]
    public async Task EmbeddingFailureFallsBackToKeywordOnly()
    {
        var db = new FakeDatabaseService { FtsResults = [Result("keyword.md", "Keyword")] };
        var service = CreateService(db, new FakeEmbeddingService { Throws = true });

        var results = await service.SearchAsync("query", 3);

        var result = Assert.Single(results);
        Assert.Equal("keyword.md", result.FilePath);
        Assert.False(db.VectorSearchCalled);
    }

    [Fact]
    public async Task EmptyQueryEmbeddingSkipsVectorSearch()
    {
        var db = new FakeDatabaseService { FtsResults = [Result("keyword.md", "Keyword")] };
        var service = CreateService(db, new FakeEmbeddingService { Vector = [] });

        var results = await service.SearchAsync("query", 3);

        Assert.Single(results);
        Assert.False(db.VectorSearchCalled);
    }
}
