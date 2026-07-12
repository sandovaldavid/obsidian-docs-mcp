using System.Collections.Generic;
using System.Threading.Tasks;
using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

public interface IDatabaseService
{
    /// <summary>Resolved absolute path to the SQLite database file backing the index.</summary>
    string DbPath { get; }

    Task InitializeDatabaseAsync();
    Task SaveChunksAsync(List<SectionChunk> chunks);
    Task<List<SearchResult>> FtsSearchAsync(string queryText, int limit);
    Task<List<SearchResult>> VectorSearchAsync(float[] queryVector, int limit);
    Task<int> GetTotalChunksCountAsync();
}
