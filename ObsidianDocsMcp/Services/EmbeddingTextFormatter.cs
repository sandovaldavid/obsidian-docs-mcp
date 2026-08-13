using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

/// <summary>
/// Single place where the exact text sent to the embedding model is built, for the document
/// side of the retrieval pair. Any change here (e.g. adding a model-specific task prefix like
/// nomic's "search_document:") invalidates every stored vector and requires a reindex. The
/// query side has no formatting today — add a matching FormatQuery here (and call it from
/// SearchService) only when the query needs its own transform (e.g. a "search_query:" prefix).
/// </summary>
public static class EmbeddingTextFormatter
{
    public static string FormatDocument(SectionChunk chunk) =>
        $"Title: {chunk.Title}\nHeader: {chunk.Header}\nContent: {chunk.Content}";
}
