using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ObsidianDocsMcp.Services;

namespace ObsidianDocsMcp.Tools;

[McpServerToolType]
public class ObsidianSearchTools
{
    private readonly IDatabaseService _dbService;
    private readonly SearchService _searchService;
    private readonly ObsidianIndexer _indexer;
    private readonly ILogger<ObsidianSearchTools> _logger;

    // The MCP C# SDK resolves this constructor using the host's DI container.
    public ObsidianSearchTools(
        IDatabaseService dbService,
        SearchService searchService,
        ObsidianIndexer indexer,
        ILogger<ObsidianSearchTools> logger)
    {
        _dbService = dbService;
        _searchService = searchService;
        _indexer = indexer;
        _logger = logger;
    }

    [McpServerTool, Description("Performs a hybrid (semantic and keyword) search over the Obsidian documentation (user and developer manuals), returning the most relevant sections.")]
    public async Task<string> SearchDocumentation(
        [Description("The user's question or a technical search term (e.g. 'how to create a plugin' or 'WorkspaceLeaf').")] string query,
        [Description("Maximum number of relevant fragments to return (default 3).")] int limit = 3)
    {
        _logger.LogInformation("MCP Tool called: SearchDocumentation with query '{Query}' and limit {Limit}", query, limit);

        try
        {
            var mergedResults = await _searchService.SearchAsync(query, limit);

            if (mergedResults.Count == 0)
            {
                return "No relevant results were found in the Obsidian documentation.";
            }

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(mergedResults, jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SearchDocumentation tool.");
            return $"Error processing the search: {ex.Message}";
        }
    }

    [McpServerTool, Description("Starts a reindex process that reads all Markdown files, generates their embeddings via Ollama, and stores them in SQLite.")]
    public Task<string> ReindexDocumentation(
        [Description("Comma-separated top-level User Help folders to index, e.g. 'en,es,Sandbox'. Omit to index every language (default).")] string? userHelpFolders = null,
        [Description("Comma-separated top-level Developer Docs folders to index. Omit to index everything (default).")] string? developerDocsFolders = null)
    {
        _logger.LogInformation("MCP Tool called: ReindexDocumentation");

        // The gate is acquired synchronously here so we can report immediately if a reindex is
        // already running, instead of allowing concurrent reindexes to race each other.
        if (!_indexer.TryBeginReindex())
        {
            return Task.FromResult("A reindex is already in progress. Wait for it to finish before starting another one (use IndexStatus to check progress).");
        }

        // Run indexing asynchronously so we don't block the MCP transport.
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexer.IndexAllDocsAsync(userHelpFolders, developerDocsFolders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background reindexation failed.");
            }
            finally
            {
                _indexer.EndReindex();
            }
        });

        return Task.FromResult("Reindexing has started in the background. This can take a few minutes depending on the number of files and Ollama's speed. Use IndexStatus to check progress or a possible error.");
    }

    [McpServerTool, Description("Returns the current status of the Obsidian documentation index, including whether a reindex is in progress and the last error if the previous reindex failed.")]
    public async Task<string> IndexStatus()
    {
        _logger.LogInformation("MCP Tool called: IndexStatus");
        try
        {
            var count = await _dbService.GetTotalChunksCountAsync();
            var status = $"The index currently has {count} documentation sections registered.";

            if (_indexer.IsReindexing)
            {
                status += " A reindex is currently in progress.";
            }
            else if (_indexer.LastReindexError != null)
            {
                status += $" The last reindex failed: {_indexer.LastReindexError}";
            }

            return status;
        }
        catch (Exception ex)
        {
            return $"Error getting index status: {ex.Message}";
        }
    }
}
