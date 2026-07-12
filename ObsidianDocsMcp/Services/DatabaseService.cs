using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;

    public string DbPath { get; }

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _logger = logger;
        var dbPath = configuration["Database:Path"];
        if (string.IsNullOrEmpty(dbPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "obsidian-docs-mcp");
            dbPath = Path.Combine(appFolder, "obsidian_docs.db");
        }

        // Ensure the database directory exists if a relative or absolute path is specified.
        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        DbPath = Path.GetFullPath(dbPath);
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeDatabaseAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // WAL mode lets readers (searches) keep seeing the last committed snapshot while a
        // writer (reindex) is mid-transaction, instead of blocking or throwing "database is locked".
        var journalModeCmd = connection.CreateCommand();
        journalModeCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await journalModeCmd.ExecuteScalarAsync();

        var busyTimeoutCmd = connection.CreateCommand();
        busyTimeoutCmd.CommandText = "PRAGMA busy_timeout=5000;";
        await busyTimeoutCmd.ExecuteNonQueryAsync();

        // 1. Metadata and chunk content table (includes the embedding BLOB)
        var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY,
                FilePath TEXT NOT NULL,
                Title TEXT NOT NULL,
                Header TEXT NOT NULL,
                Content TEXT NOT NULL,
                Embedding BLOB NULL
            );";
        await createTableCmd.ExecuteNonQueryAsync();

        // 2. FTS5 virtual table for fast keyword search
        var createFtsCmd = connection.CreateCommand();
        createFtsCmd.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS ChunksFTS USING fts5(
                ChunkId UNINDEXED,
                Title,
                Header,
                Content,
                tokenize = 'porter unicode61'
            );";
        await createFtsCmd.ExecuteNonQueryAsync();

        _logger.LogInformation("Database initialized successfully.");
    }

    public async Task SaveChunksAsync(List<SectionChunk> chunks)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            // Clearing and re-inserting inside the same transaction as the new data means
            // readers never observe an empty/partial index: they keep seeing the previous
            // complete index right up until this transaction commits atomically, and if
            // anything below throws, the rollback leaves the previous index untouched.
            var deleteChunksCmd = connection.CreateCommand();
            deleteChunksCmd.Transaction = transaction;
            deleteChunksCmd.CommandText = "DELETE FROM Chunks;";
            await deleteChunksCmd.ExecuteNonQueryAsync();

            var deleteFtsCmd = connection.CreateCommand();
            deleteFtsCmd.Transaction = transaction;
            deleteFtsCmd.CommandText = "DELETE FROM ChunksFTS;";
            await deleteFtsCmd.ExecuteNonQueryAsync();

            int insertedCount = 0;
            foreach (var chunk in chunks)
            {
                // Save to the main table
                var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT OR REPLACE INTO Chunks (Id, FilePath, Title, Header, Content, Embedding)
                    VALUES ($id, $filePath, $title, $header, $content, $embedding);";
                insertCmd.Parameters.AddWithValue("$id", chunk.Id);
                insertCmd.Parameters.AddWithValue("$filePath", chunk.FilePath);
                insertCmd.Parameters.AddWithValue("$title", chunk.Title);
                insertCmd.Parameters.AddWithValue("$header", chunk.Header);
                insertCmd.Parameters.AddWithValue("$content", chunk.Content);

                byte[]? embeddingBytes = null;
                if (chunk.Embedding != null && chunk.Embedding.Length > 0)
                {
                    embeddingBytes = new byte[chunk.Embedding.Length * sizeof(float)];
                    Buffer.BlockCopy(chunk.Embedding, 0, embeddingBytes, 0, embeddingBytes.Length);
                }
                insertCmd.Parameters.AddWithValue("$embedding", (object?)embeddingBytes ?? DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync();

                // Save to the FTS table
                var insertFtsCmd = connection.CreateCommand();
                insertFtsCmd.Transaction = transaction;
                insertFtsCmd.CommandText = @"
                    INSERT INTO ChunksFTS (ChunkId, Title, Header, Content)
                    VALUES ($id, $title, $header, $content);";
                insertFtsCmd.Parameters.AddWithValue("$id", chunk.Id);
                insertFtsCmd.Parameters.AddWithValue("$title", chunk.Title);
                insertFtsCmd.Parameters.AddWithValue("$header", chunk.Header);
                insertFtsCmd.Parameters.AddWithValue("$content", chunk.Content);
                await insertFtsCmd.ExecuteNonQueryAsync();

                insertedCount++;
                if (insertedCount % 500 == 0)
                {
                    _logger.LogInformation("Inserted {Progress}/{Total} chunks into database...", insertedCount, chunks.Count);
                }
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Successfully replaced the index with {Count} chunks.", chunks.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error saving chunks to database. Transaction rolled back — the previous index is left intact.");
            throw;
        }
    }

    public async Task<List<SearchResult>> FtsSearchAsync(string queryText, int limit)
    {
        var results = new List<SearchResult>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var searchCmd = connection.CreateCommand();
        // We use SQLite FTS5's MATCH operator.
        // bm25() is FTS5's native ranking function (lower is better), so we invert it below to
        // fit our similarity scale.
        searchCmd.CommandText = @"
            SELECT fts.Title, fts.Header, fts.Content, c.FilePath, bm25(ChunksFTS) as rank
            FROM ChunksFTS fts
            JOIN Chunks c ON fts.ChunkId = c.Id
            WHERE ChunksFTS MATCH $query
            ORDER BY rank ASC
            LIMIT $limit;";

        // Simple escaping of characters that would otherwise break FTS5 syntax.
        var sanitizedQuery = queryText.Replace("'", "''");
        searchCmd.Parameters.AddWithValue("$query", sanitizedQuery);
        searchCmd.Parameters.AddWithValue("$limit", limit);

        try
        {
            using var reader = await searchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var bm25Rank = reader.GetDouble(4);
                // Convert the bm25 rank into an approximate 0-1 similarity score for comparison.
                // In bm25, a smaller (more negative) number indicates higher relevance.
                double score = 1.0 / (1.0 + Math.Exp(bm25Rank));

                results.Add(new SearchResult
                {
                    Title = reader.GetString(0),
                    Header = reader.GetString(1),
                    Content = reader.GetString(2),
                    FilePath = reader.GetString(3),
                    MatchPercent = Math.Clamp(score * 100, 0, 100),
                    SourceType = "Keyword"
                });
            }
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "FTS search failed. Returning empty list. This can happen if query syntax is incomplete.");
        }

        return results;
    }

    public async Task<List<SearchResult>> VectorSearchAsync(float[] queryVector, int limit)
    {
        if (limit <= 0)
        {
            return new List<SearchResult>();
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT FilePath, Title, Header, Content, Embedding FROM Chunks WHERE Embedding IS NOT NULL;";

        // Bounded top-K selection: score each row as it streams from the reader and keep only
        // the best `limit` results in a min-heap, instead of materializing every chunk +
        // embedding into a list and doing a full O(n log n) sort over the whole corpus.
        var topK = new PriorityQueue<SearchResult, double>();

        using var reader = await selectCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var filePath = reader.GetString(0);
            var title = reader.GetString(1);
            var header = reader.GetString(2);
            var content = reader.GetString(3);

            using var blobStream = reader.GetStream(4);
            using var ms = new MemoryStream();
            await blobStream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var embedding = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);

            double similarity = CosineSimilarity(queryVector, embedding);

            var result = new SearchResult
            {
                FilePath = filePath,
                Title = title,
                Header = header,
                Content = content,
                MatchPercent = Math.Clamp(similarity * 100, 0, 100),
                SourceType = "Semantic"
            };

            if (topK.Count < limit)
            {
                topK.Enqueue(result, similarity);
            }
            else if (topK.TryPeek(out _, out var lowestScore) && similarity > lowestScore)
            {
                topK.EnqueueDequeue(result, similarity);
            }
        }

        // PriorityQueue is a min-heap, so draining it yields ascending score order.
        var ordered = new List<SearchResult>(topK.Count);
        while (topK.TryDequeue(out var item, out _))
        {
            ordered.Add(item);
        }
        ordered.Reverse();

        return ordered;
    }

    public async Task<int> GetTotalChunksCountAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Chunks;";
        var count = await countCmd.ExecuteScalarAsync();
        return count != null ? Convert.ToInt32(count) : 0;
    }

    private static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            return 0.0;
        }

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        if (normA == 0.0 || normB == 0.0)
        {
            return 0.0;
        }

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
