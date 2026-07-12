using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Markdig.Syntax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

public class ObsidianIndexer
{
    private const long MaxZipDownloadBytes = 200 * 1024 * 1024; // 200 MB — official doc repos are a few MB at most
    private const long MaxZipEntryBytes = 5 * 1024 * 1024; // 5 MB per markdown file
    private const int EmbeddingConcurrency = 4;

    private readonly IDatabaseService _dbService;
    private readonly IEmbeddingService _embeddingService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ObsidianIndexer> _logger;

    private readonly string _developerDocsPath;
    private readonly string _userHelpPath;
    private readonly string _developerDocsZipUrl;
    private readonly string _userHelpZipUrl;
    private readonly HashSet<string> _developerDocsIncludeFolders;
    private readonly HashSet<string> _userHelpIncludeFolders;
    private readonly bool _usePrebuiltIndex;
    private readonly string _prebuiltIndexUrl;

    // Guards against overlapping reindex runs (ObsidianIndexer is a singleton, so this state
    // persists across MCP tool calls) and surfaces background-task outcomes to IndexStatus,
    // since ReindexDocumentation returns immediately and can't report failures directly.
    private readonly SemaphoreSlim _reindexGate = new(1, 1);

    public bool IsReindexing => _reindexGate.CurrentCount == 0;
    public string? LastReindexError { get; private set; }

    public ObsidianIndexer(
        IDatabaseService dbService,
        IEmbeddingService embeddingService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ObsidianIndexer> logger)
    {
        _dbService = dbService;
        _embeddingService = embeddingService;
        _httpClient = httpClient;
        _logger = logger;

        // Optional local paths
        _developerDocsPath = configuration["Docs:DeveloperDocsPath"] ?? "../obsidian-developer-docs";
        _userHelpPath = configuration["Docs:UserHelpPath"] ?? "../obsidian-help";

        // Default GitHub ZIP download URLs
        _developerDocsZipUrl = configuration["Docs:DeveloperDocsZipUrl"] ?? "https://github.com/obsidianmd/obsidian-developer-docs/archive/refs/heads/main.zip";
        _userHelpZipUrl = configuration["Docs:UserHelpZipUrl"] ?? "https://github.com/obsidianmd/obsidian-help/archive/refs/heads/master.zip";

        // Optional persistent top-level folder restriction (e.g. "en,es,Sandbox"). Empty means
        // no restriction — every top-level folder is indexed, which is the default behavior.
        _developerDocsIncludeFolders = ParseFolderList(configuration["Docs:DeveloperDocsIncludeFolders"]);
        _userHelpIncludeFolders = ParseFolderList(configuration["Docs:UserHelpIncludeFolders"]);

        // Prebuilt index: a periodically-regenerated SQLite DB (with embeddings already computed)
        // published to a fixed, dedicated release tag — distinct from this repo's normal vX.Y.Z-beta
        // software releases — so first-run users don't have to wait for a live reindex.
        _usePrebuiltIndex = !bool.TryParse(configuration["Docs:UsePrebuiltIndex"], out var usePrebuilt) || usePrebuilt;
        var configuredPrebuiltIndexUrl = configuration["Docs:PrebuiltIndexUrl"];
        _prebuiltIndexUrl = string.IsNullOrEmpty(configuredPrebuiltIndexUrl)
            ? "https://github.com/sandovaldavid/obsidian-docs-mcp/releases/download/docs-index/obsidian_docs.db"
            : configuredPrebuiltIndexUrl;
    }

    private static HashSet<string> ParseFolderList(string? raw) =>
        new(
            (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts to acquire the reindex lock without blocking. Callers must call
    /// <see cref="EndReindex"/> exactly once (typically in a finally block) if this returns true.
    /// </summary>
    public bool TryBeginReindex() => _reindexGate.Wait(0);

    public void EndReindex() => _reindexGate.Release();

    /// <summary>
    /// Downloads the periodically-regenerated prebuilt index (SQLite DB with embeddings already
    /// computed) from its fixed release URL and writes it to the configured DB path, so a fresh
    /// install doesn't have to pay for a live reindex. Returns false (without throwing) if
    /// disabled via config, or on any download/write failure — callers should fall back to a
    /// live reindex in that case.
    /// </summary>
    public async Task<bool> TryDownloadPrebuiltIndexAsync()
    {
        if (!_usePrebuiltIndex)
        {
            return false;
        }

        try
        {
            _logger.LogInformation("Downloading prebuilt documentation index from {Url}...", _prebuiltIndexUrl);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ObsidianDocsMcp-Client");

            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await _httpClient.GetAsync(_prebuiltIndexUrl, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("No prebuilt index available (HTTP {Status}).", (int)response.StatusCode);
                return false;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxZipDownloadBytes)
            {
                _logger.LogWarning("Refusing to download prebuilt index: reported size {Size} bytes exceeds the {Max} byte limit.", contentLength.Value, MaxZipDownloadBytes);
                return false;
            }

            // Download to a temp file first so a failure/timeout partway through never leaves a
            // truncated database at the real path.
            var tempPath = _dbService.DbPath + ".download";
            await using (var fileStream = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fileStream, downloadCts.Token);
            }
            File.Move(tempPath, _dbService.DbPath, overwrite: true);

            _logger.LogInformation("Prebuilt documentation index downloaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not download the prebuilt index. Falling back to live indexing.");
            return false;
        }
    }

    /// <summary>
    /// Indexes all documentation sources.
    /// </summary>
    /// <param name="userHelpFolders">
    /// Optional comma-separated list of top-level User Help folders to restrict indexing to
    /// (e.g. "en,es,Sandbox"), overriding the persistent <c>Docs:UserHelpIncludeFolders</c>
    /// config for this run only. Null falls back to that config; both null/empty mean "index
    /// every folder" (the default).
    /// </param>
    /// <param name="developerDocsFolders">Same as <paramref name="userHelpFolders"/>, but for Developer Docs.</param>
    public async Task IndexAllDocsAsync(string? userHelpFolders = null, string? developerDocsFolders = null)
    {
        LastReindexError = null;

        try
        {
            _logger.LogInformation("Starting documentation indexation process...");
            await _dbService.InitializeDatabaseAsync();

            var developerDocsIncludeFolders = developerDocsFolders != null ? ParseFolderList(developerDocsFolders) : _developerDocsIncludeFolders;
            var userHelpIncludeFolders = userHelpFolders != null ? ParseFolderList(userHelpFolders) : _userHelpIncludeFolders;

            var chunks = new List<SectionChunk>();

            // 1. Process Developer Docs (local or GitHub)
            if (Directory.Exists(_developerDocsPath))
            {
                _logger.LogInformation("Processing developer docs locally at: {Path}", Path.GetFullPath(_developerDocsPath));
                await ProcessLocalDirectoryAsync(_developerDocsPath, "Developer Docs", chunks, developerDocsIncludeFolders);
            }
            else
            {
                _logger.LogInformation("Developer docs local path does not exist. Fetching from GitHub ZIP: {Url}", _developerDocsZipUrl);
                await ProcessGithubZipAsync(_developerDocsZipUrl, "Developer Docs", chunks, developerDocsIncludeFolders);
            }

            // 2. Process User Help (local or GitHub)
            if (Directory.Exists(_userHelpPath))
            {
                _logger.LogInformation("Processing user help docs locally at: {Path}", Path.GetFullPath(_userHelpPath));
                await ProcessLocalDirectoryAsync(_userHelpPath, "User Help", chunks, userHelpIncludeFolders);
            }
            else
            {
                _logger.LogInformation("User help docs local path does not exist. Fetching from GitHub ZIP: {Url}", _userHelpZipUrl);
                await ProcessGithubZipAsync(_userHelpZipUrl, "User Help", chunks, userHelpIncludeFolders);
            }

            if (chunks.Count == 0)
            {
                _logger.LogWarning("No markdown files found to index.");
                return;
            }

            _logger.LogInformation("Found {Count} chunks. Generating embeddings via Ollama (concurrency={Concurrency})... The first few requests may be slower while Ollama loads the embedding model into memory.", chunks.Count, EmbeddingConcurrency);

            int successCount = 0;
            int processedCount = 0;
            using var embeddingGate = new SemaphoreSlim(EmbeddingConcurrency);

            var embeddingTasks = chunks.Select(async chunk =>
            {
                await embeddingGate.WaitAsync();
                try
                {
                    string textToEmbed = $"Title: {chunk.Title}\nHeader: {chunk.Header}\nContent: {chunk.Content}";
                    chunk.Embedding = await _embeddingService.GetEmbeddingAsync(textToEmbed);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not generate embedding for chunk {Id} in {File}. Error: {Msg}. Falling back to keyword search only.", chunk.Id, chunk.FilePath, ex.Message);
                }
                finally
                {
                    var processed = Interlocked.Increment(ref processedCount);
                    if (processed % 100 == 0)
                    {
                        _logger.LogInformation("Generated embeddings for {Progress}/{Total} chunks...", processed, chunks.Count);
                    }
                    embeddingGate.Release();
                }
            });

            await Task.WhenAll(embeddingTasks);

            _logger.LogInformation("Saving chunks to database...");
            await _dbService.SaveChunksAsync(chunks);
            _logger.LogInformation("Documentation indexation completed. {Indexed}/{Total} chunks successfully vectorized.", successCount, chunks.Count);
        }
        catch (Exception ex)
        {
            LastReindexError = ex.Message;
            throw;
        }
    }

    private async Task ProcessLocalDirectoryAsync(string directoryPath, string docSourceType, List<SectionChunk> accumulatedChunks, IReadOnlySet<string> includeFolders)
    {
        var mdFiles = Directory.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/.obsidian/") && !f.Contains("\\.obsidian\\") && !f.Contains(".git"))
            .ToList();

        int matchedCount = 0;
        foreach (var file in mdFiles)
        {
            var relativePath = Path.GetRelativePath(directoryPath, file);

            if (includeFolders.Count > 0 && !includeFolders.Contains(TopLevelFolder(relativePath)))
            {
                continue;
            }

            var title = Path.GetFileNameWithoutExtension(file);
            var fileContent = await File.ReadAllTextAsync(file);

            var fileChunks = SegmentMarkdown(fileContent, relativePath, title, docSourceType);
            accumulatedChunks.AddRange(fileChunks);
            matchedCount++;
        }

        LogFolderFilterSummary(docSourceType, matchedCount, mdFiles.Count, includeFolders);
    }

    /// <summary>Top-level path segment used to match a file against an include-folder filter.</summary>
    private static string TopLevelFolder(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var firstSlashIdx = normalized.IndexOf('/');
        return firstSlashIdx == -1 ? normalized : normalized[..firstSlashIdx];
    }

    private void LogFolderFilterSummary(string docSourceType, int matched, int total, IReadOnlySet<string> includeFolders)
    {
        if (includeFolders.Count == 0)
        {
            _logger.LogInformation("{Source}: no folder filter applied, kept all {Total} files.", docSourceType, total);
        }
        else
        {
            _logger.LogInformation("{Source}: kept {Matched}/{Total} files (folders: {Folders}).", docSourceType, matched, total, string.Join(", ", includeFolders));
        }
    }

    private async Task ProcessGithubZipAsync(string zipUrl, string docSourceType, List<SectionChunk> accumulatedChunks, IReadOnlySet<string> includeFolders)
    {
        try
        {
            _logger.LogInformation("Downloading documentation ZIP from GitHub...");
            // Set the User-Agent header required by the GitHub API
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ObsidianDocsMcp-Client");

            // HttpClient.Timeout only bounds the wait for response headers when using
            // ResponseHeadersRead; the body read below has no built-in limit otherwise, so a
            // stalled/throttled connection can hang this call forever with near-zero CPU usage.
            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await _httpClient.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxZipDownloadBytes)
            {
                _logger.LogError("Refusing to download ZIP from {Url}: reported size {Size} bytes exceeds the {Max} byte limit.", zipUrl, contentLength.Value, MaxZipDownloadBytes);
                return;
            }

            using var zipBuffer = new MemoryStream();
            await response.Content.CopyToAsync(zipBuffer, downloadCts.Token);
            zipBuffer.Position = 0;
            using var archive = new ZipArchive(zipBuffer);

            _logger.LogInformation("Parsing ZIP archive in memory...");
            int totalCount = 0;
            int matchedCount = 0;

            foreach (var entry in archive.Entries)
            {
                // Only process markdown files that are not in hidden directories (like .github or .obsidian)
                if (entry.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.Contains("/.obsidian/") &&
                    !entry.FullName.Contains("/.github/"))
                {
                    totalCount++;

                    if (entry.Length > MaxZipEntryBytes)
                    {
                        _logger.LogWarning("Skipping oversized entry {Entry} ({Size} bytes, limit {Max}).", entry.FullName, entry.Length, MaxZipEntryBytes);
                        continue;
                    }

                    // Remove the root folder GitHub creates in the ZIP (e.g. repo-name-branch-name/)
                    var cleanPath = entry.FullName;
                    var firstSlashIdx = entry.FullName.IndexOf('/');
                    if (firstSlashIdx != -1)
                    {
                        cleanPath = entry.FullName[(firstSlashIdx + 1)..];
                    }

                    // Skip entries outside the requested top-level folders before even reading
                    // their content, so excluded files never reach chunking/embedding.
                    if (includeFolders.Count > 0 && !includeFolders.Contains(TopLevelFolder(cleanPath)))
                    {
                        continue;
                    }

                    var title = Path.GetFileNameWithoutExtension(entry.Name);

                    using var reader = new StreamReader(entry.Open());
                    var fileContent = await reader.ReadToEndAsync();

                    var fileChunks = SegmentMarkdown(fileContent, cleanPath, title, docSourceType);
                    accumulatedChunks.AddRange(fileChunks);
                    matchedCount++;
                }
            }

            _logger.LogInformation("Successfully parsed {FileCount} Markdown files from GitHub ZIP.", matchedCount);
            LogFolderFilterSummary(docSourceType, matchedCount, totalCount, includeFolders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching or parsing GitHub ZIP from {Url}", zipUrl);
        }
    }

    /// <summary>
    /// Splits markdown into logical sections using Markdig's parsed heading spans (levels 1-4),
    /// so headings inside fenced code blocks are correctly ignored and setext headings are
    /// detected too — cases a hand-rolled line scan misses.
    /// </summary>
    private List<SectionChunk> SegmentMarkdown(string markdown, string relativePath, string title, string sourceName)
    {
        var chunks = new List<SectionChunk>();
        int chunkIndex = 0;

        void FlushChunk(string header, string rawContent)
        {
            var trimmed = rawContent.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            // Safety split for very long sections, same 2000-char budget as before.
            var needsSplit = trimmed.Length > 2000;
            for (int offset = 0; offset < trimmed.Length; offset += 2000)
            {
                var partLength = Math.Min(2000, trimmed.Length - offset);
                var part = trimmed.Substring(offset, partLength);
                var partHeader = needsSplit ? $"{header} (Part {(offset / 2000) + 1})" : header;
                var chunkId = GenerateHash($"{relativePath}_{chunkIndex}_{partHeader}");
                chunks.Add(new SectionChunk
                {
                    Id = chunkId,
                    FilePath = relativePath,
                    Title = $"{sourceName} > {title}",
                    Header = partHeader,
                    Content = part
                });
                chunkIndex++;
            }
        }

        var document = Markdig.Markdown.Parse(markdown);
        var headings = document.Descendants<HeadingBlock>()
            .Where(h => h.Level <= 4)
            .OrderBy(h => h.Span.Start)
            .ToList();

        if (headings.Count == 0)
        {
            FlushChunk("General", markdown);
            return chunks;
        }

        if (headings[0].Span.Start > 0)
        {
            FlushChunk("General", markdown[..headings[0].Span.Start]);
        }

        for (int i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            // For setext headings (underlined with ===/---), Span covers both the text line and
            // the underline, so only take the first line to get just the heading text.
            var rawHeadingSpan = markdown.Substring(heading.Span.Start, heading.Span.Length);
            var firstLineEnd = rawHeadingSpan.IndexOfAny(['\r', '\n']);
            var headingText = (firstLineEnd >= 0 ? rawHeadingSpan[..firstLineEnd] : rawHeadingSpan).TrimStart('#').Trim();

            var contentStart = heading.Span.End + 1;
            var contentEnd = i + 1 < headings.Count ? headings[i + 1].Span.Start : markdown.Length;
            contentEnd = Math.Min(contentEnd, markdown.Length);

            if (contentStart >= contentEnd)
            {
                continue;
            }

            var sectionContent = markdown[contentStart..contentEnd];
            FlushChunk(string.IsNullOrWhiteSpace(headingText) ? "General" : headingText, sectionContent);
        }

        return chunks;
    }

    private static string GenerateHash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLower();
    }
}
