using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ObsidianDocsMcp.Models;
using ObsidianDocsMcp.Services;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public sealed class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"obsidian-docs-mcp-tests-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = _dbPath })
            .Build();
        _db = new DatabaseService(configuration, NullLogger<DatabaseService>.Instance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_dbPath + suffix);
        }
    }

    private static SectionChunk Chunk(string id, string content, float[]? embedding = null, string header = "General") => new()
    {
        Id = id,
        FilePath = $"en/{id}.md",
        Title = $"User Help > {id}",
        Header = header,
        Content = content,
        Embedding = embedding
    };

    [Fact]
    public async Task InitializeDatabase_IsIdempotent()
    {
        await _db.InitializeDatabaseAsync();
        await _db.InitializeDatabaseAsync();

        Assert.Equal(0, await _db.GetTotalChunksCountAsync());
    }

    [Fact]
    public async Task SaveChunks_RoundTripsContentAndEmbedding()
    {
        await _db.InitializeDatabaseAsync();
        var embedding = new float[] { 0.25f, -0.5f, 1.5f };
        await _db.SaveChunksAsync([Chunk("note", "Some content", embedding)]);

        Assert.Equal(1, await _db.GetTotalChunksCountAsync());

        // Searching with the exact stored vector must decode the BLOB back to the same floats,
        // which shows up as a cosine similarity of 1 (MatchPercent 100).
        var results = await _db.VectorSearchAsync(embedding, 5);
        var result = Assert.Single(results);
        Assert.Equal("en/note.md", result.FilePath);
        Assert.Equal("Some content", result.Content);
        Assert.Equal(100, result.MatchPercent, 3);
        Assert.Equal("Semantic", result.SourceType);
    }

    [Fact]
    public async Task SaveChunks_ReplacesThePreviousIndexEntirely()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync([Chunk("old1", "old"), Chunk("old2", "old")]);
        await _db.SaveChunksAsync([Chunk("new1", "new")]);

        Assert.Equal(1, await _db.GetTotalChunksCountAsync());
        var results = await _db.FtsSearchAsync("new", 10);
        Assert.All(results, r => Assert.Equal("en/new1.md", r.FilePath));
    }

    [Fact]
    public async Task FtsSearch_AppliesPorterStemming()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync([Chunk("cfg", "This section covers configuration of the plugin.")]);

        var results = await _db.FtsSearchAsync("configuring", 10);

        var result = Assert.Single(results);
        Assert.Equal("en/cfg.md", result.FilePath);
        Assert.Equal("Keyword", result.SourceType);
    }

    [Fact]
    public async Task FtsSearch_MalformedQueryReturnsEmptyListInsteadOfThrowing()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync([Chunk("doc", "content")]);

        var results = await _db.FtsSearchAsync("\"unclosed AND ((", 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task FtsSearch_TreatsPunctuationAsText()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync([Chunk("manifest", "The manifest.json file defines a plugin.")]);

        var results = await _db.FtsSearchAsync("manifest.json", 10);

        Assert.Single(results);
        Assert.Equal("en/manifest.md", results[0].FilePath);
    }

    [Fact]
    public async Task VectorSearch_ReturnsTopKInDescendingSimilarityOrder()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync(
        [
            Chunk("exact", "a", [1f, 0f]),
            Chunk("close", "b", [1f, 0.5f]),
            Chunk("far", "c", [0f, 1f])
        ]);

        var results = await _db.VectorSearchAsync([1f, 0f], 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("en/exact.md", results[0].FilePath);
        Assert.Equal("en/close.md", results[1].FilePath);
    }

    [Fact]
    public async Task VectorSearch_SkipsChunksWithoutEmbedding()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync(
        [
            Chunk("vectorized", "a", [1f, 0f]),
            Chunk("keyword-only", "b", embedding: null)
        ]);

        var results = await _db.VectorSearchAsync([1f, 0f], 10);

        var result = Assert.Single(results);
        Assert.Equal("en/vectorized.md", result.FilePath);
    }

    [Fact]
    public async Task VectorSearch_NonPositiveLimitReturnsEmpty()
    {
        await _db.InitializeDatabaseAsync();
        await _db.SaveChunksAsync([Chunk("doc", "a", [1f, 0f])]);

        Assert.Empty(await _db.VectorSearchAsync([1f, 0f], 0));
    }
}
