using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

/// <summary>
/// Single place where the exact text sent to the embedding model is built, for both sides of
/// the retrieval pair. Any change here (e.g. adding model-specific task prefixes like nomic's
/// "search_document:" / "search_query:") invalidates every stored vector and requires a reindex.
/// </summary>
public static class EmbeddingTextFormatter
{
    public static string FormatDocument(SectionChunk chunk) =>
        $"Title: {chunk.Title}\nHeader: {chunk.Header}\nContent: {chunk.Content}";

    public static string FormatQuery(string query) => query;
}
