using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObsidianDocsMcp.Eval;
using ObsidianDocsMcp.Services;

// Retrieval-quality evaluation harness. Builds a pinned-snapshot index, runs the golden set
// through the same SearchService the MCP tool uses, and compares metric reports so search
// changes (model, prefixes, chunking, thresholds) are adopted on data instead of vibes.
//
//   build-index --golden-set eval/golden-set.json --db /tmp/eval.db [--model nomic-embed-text]
//               [--user-help-folders en] [--developer-docs-folders ""]
//   run         --golden-set eval/golden-set.json --db /tmp/eval.db --label baseline
//               [--output eval/results] [--depth 10]
//   compare     --baseline eval/baseline.json --candidate eval/results/candidate.json
//               [--fail-on-regression] [--epsilon 0.02]

var headlineMetrics = new[] { "recall@3", "mrr" };

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

var command = args[0];
var options = ParseOptions(args.Skip(1));

try
{
    switch (command)
    {
        case "build-index":
            await BuildIndexAsync(options);
            return 0;
        case "validate":
            return Validate(options);
        case "run":
            await RunAsync(options);
            return 0;
        case "compare":
            return Compare(options);
        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            PrintUsage();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

async Task BuildIndexAsync(Dictionary<string, string> opts)
{
    var goldenSet = GoldenSet.Load(Require(opts, "golden-set"));
    var dbPath = Require(opts, "db");

    var settings = new Dictionary<string, string?>
    {
        ["Database:Path"] = dbPath,
        // Always index from the pinned snapshot, never a prebuilt DB or local checkout.
        ["Docs:UsePrebuiltIndex"] = "false",
        ["Docs:DeveloperDocsPath"] = "",
        ["Docs:UserHelpPath"] = ""
    };
    if (!string.IsNullOrEmpty(goldenSet.DocsSnapshot?.UserHelpZipUrl))
    {
        settings["Docs:UserHelpZipUrl"] = goldenSet.DocsSnapshot.UserHelpZipUrl;
    }
    if (!string.IsNullOrEmpty(goldenSet.DocsSnapshot?.DeveloperDocsZipUrl))
    {
        settings["Docs:DeveloperDocsZipUrl"] = goldenSet.DocsSnapshot.DeveloperDocsZipUrl;
    }
    if (opts.TryGetValue("model", out var model))
    {
        settings["Ollama:Model"] = model;
    }

    using var provider = BuildServiceProvider(settings);
    var db = provider.GetRequiredService<IDatabaseService>();
    await db.InitializeDatabaseAsync();

    var indexer = provider.GetRequiredService<ObsidianIndexer>();
    // The golden set is English-only, so restricting User Help to `en` by default keeps eval
    // index builds fast; pass --user-help-folders to widen it (production indexes en,es,Sandbox).
    var userHelpFolders = opts.GetValueOrDefault("user-help-folders", "en");
    var developerDocsFolders = opts.GetValueOrDefault("developer-docs-folders", "");
    await indexer.IndexAllDocsAsync(userHelpFolders, developerDocsFolders);

    Console.WriteLine($"Index built at {dbPath} with {await db.GetTotalChunksCountAsync()} chunks.");
}

int Validate(Dictionary<string, string> opts)
{
    var goldenSet = GoldenSet.Load(Require(opts, "golden-set"));
    var dbPath = Require(opts, "db");

    // Annotated paths go stale when the upstream docs restructure, so every eval run validates
    // them against the actual indexed corpus first — a silently-missing path would otherwise
    // just read as a retrieval miss and poison the metrics.
    var indexedDocs = new Dictionary<string, HashSet<string>>();
    var originalByNormalized = new Dictionary<string, string>();
    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FilePath, Header FROM Chunks;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var normalized = Metrics.NormalizePath(path);
            if (!indexedDocs.TryGetValue(normalized, out var headers))
            {
                headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                indexedDocs[normalized] = headers;
            }
            headers.Add(reader.GetString(1));
            originalByNormalized[normalized] = path;
        }
    }

    var missing = 0;
    foreach (var query in goldenSet.Queries)
    {
        foreach (var doc in query.Relevant)
        {
            var normalizedPath = Metrics.NormalizePath(doc.FilePath);
            if (!indexedDocs.TryGetValue(normalizedPath, out var headers))
            {
                missing++;
                Console.WriteLine($"MISSING PATH [{query.Id}] {doc.FilePath}");
                var fileName = Path.GetFileName(normalizedPath);
                var suggestions = originalByNormalized
                    .Where(kv => Path.GetFileName(kv.Key) == fileName)
                    .Select(kv => kv.Value)
                    .Take(3)
                    .ToList();
                suggestions.ForEach(s => Console.WriteLine($"  did you mean: {s}"));
                continue;
            }

            if (!string.IsNullOrEmpty(doc.Header) && !headers.Any(header => header.StartsWith(doc.Header, StringComparison.OrdinalIgnoreCase)))
            {
                missing++;
                Console.WriteLine($"MISSING HEADER [{query.Id}] {doc.FilePath} :: {doc.Header}");
            }
        }
    }

    if (missing > 0)
    {
        Console.WriteLine($"{missing} annotated path/header value(s) not present in the index ({indexedDocs.Count} indexed files). Fix eval/golden-set.json before running metrics.");
        return 1;
    }

    Console.WriteLine($"All annotated paths and headers exist in the index ({goldenSet.Queries.Count} queries, {indexedDocs.Count} indexed files).");
    return 0;
}

async Task RunAsync(Dictionary<string, string> opts)
{
    var goldenSet = GoldenSet.Load(Require(opts, "golden-set"));
    var dbPath = Require(opts, "db");
    var label = Require(opts, "label");
    ValidateLabel(label);
    var outputDir = opts.GetValueOrDefault("output", Path.Combine("eval", "results"));
    var depth = int.Parse(opts.GetValueOrDefault("depth", "10"));

    var settings = new Dictionary<string, string?> { ["Database:Path"] = dbPath };
    if (opts.TryGetValue("model", out var model))
    {
        settings["Ollama:Model"] = model;
    }

    using var provider = BuildServiceProvider(settings);
    var searchService = provider.GetRequiredService<SearchService>();
    var db = provider.GetRequiredService<IDatabaseService>();
    var config = provider.GetRequiredService<IConfiguration>();

    var perQuery = new List<QueryEvalResult>();
    var totalLatencyMs = 0.0;

    foreach (var query in goldenSet.Queries)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = await searchService.SearchAsync(query.Query, depth);
        stopwatch.Stop();
        totalLatencyMs += stopwatch.Elapsed.TotalMilliseconds;

        perQuery.Add(new QueryEvalResult
        {
            Id = query.Id,
            Query = query.Query,
            Metrics = Metrics.Evaluate(query, results),
            TopResults = results.Select(r => $"{r.FilePath} :: {r.Header}").ToList()
        });
    }

    var report = new EvalRunResult
    {
        Label = label,
        TimestampUtc = DateTime.UtcNow,
        EmbeddingModel = config["Ollama:Model"] ?? "nomic-embed-text",
        ChunkCount = await db.GetTotalChunksCountAsync(),
        MeanSearchLatencyMs = Math.Round(totalLatencyMs / goldenSet.Queries.Count, 1),
        Metrics = Metrics.Aggregate(perQuery.Select(q => q.Metrics).ToList()),
        PerQuery = perQuery
    };

    Directory.CreateDirectory(outputDir);
    var outputPath = Path.Combine(outputDir, $"{label}.json");
    File.WriteAllText(outputPath, JsonSerializer.Serialize(report, GoldenSet.JsonOptions));

    Console.WriteLine($"# Eval run: {label}");
    Console.WriteLine();
    Console.WriteLine($"Model: {report.EmbeddingModel} | Chunks: {report.ChunkCount} | Queries: {perQuery.Count} | Mean latency: {report.MeanSearchLatencyMs} ms");
    Console.WriteLine();
    Console.WriteLine("| Metric | Value |");
    Console.WriteLine("|---|---|");
    foreach (var (metric, value) in OrderedMetrics(report.Metrics))
    {
        Console.WriteLine($"| {metric} | {value:F4} |");
    }
    Console.WriteLine();
    Console.WriteLine($"Report written to {outputPath}");
}

int Compare(Dictionary<string, string> opts)
{
    var baselinePath = Require(opts, "baseline");
    var candidatePath = Require(opts, "candidate");
    var failOnRegression = opts.ContainsKey("fail-on-regression");
    var epsilon = double.Parse(opts.GetValueOrDefault("epsilon", "0.02"));

    var baseline = LoadReport(baselinePath);
    var candidate = LoadReport(candidatePath);

    Console.WriteLine($"# Compare: {baseline.Label} (baseline) vs {candidate.Label} (candidate)");
    Console.WriteLine();
    Console.WriteLine("| Metric | Baseline | Candidate | Delta |");
    Console.WriteLine("|---|---|---|---|");

    var regressions = new List<string>();
    foreach (var (metric, baseValue) in OrderedMetrics(baseline.Metrics))
    {
        var candValue = candidate.Metrics.GetValueOrDefault(metric);
        var delta = candValue - baseValue;
        Console.WriteLine($"| {metric} | {baseValue:F4} | {candValue:F4} | {delta:+0.0000;-0.0000;0.0000} |");

        if (headlineMetrics.Contains(metric) && delta < -epsilon)
        {
            regressions.Add($"{metric} dropped {-delta:F4} (allowed {epsilon:F4})");
        }
    }

    if (regressions.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("REGRESSIONS on headline metrics:");
        regressions.ForEach(r => Console.WriteLine($"  - {r}"));
        return failOnRegression ? 1 : 0;
    }

    Console.WriteLine();
    Console.WriteLine("No regressions on headline metrics (recall@3, mrr).");
    return 0;
}

static EvalRunResult LoadReport(string path) =>
    JsonSerializer.Deserialize<EvalRunResult>(File.ReadAllText(path), GoldenSet.JsonOptions)
        ?? throw new InvalidOperationException($"Could not parse eval report at {path}.");

static IEnumerable<(string Metric, double Value)> OrderedMetrics(Dictionary<string, double> metrics) =>
    metrics.OrderBy(kv => kv.Key.Contains('@') ? kv.Key.Split('@')[0] : "zz" + kv.Key)
        .ThenBy(kv => kv.Key.Contains('@') ? int.Parse(kv.Key.Split('@')[1]) : 0)
        .Select(kv => (kv.Key, kv.Value));

static ServiceProvider BuildServiceProvider(Dictionary<string, string?> settings)
{
    var configuration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddInMemoryCollection(settings)
        .Build();

    // Mirrors the main Program.cs service graph, minus the MCP transport.
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    services.AddSingleton<IConfiguration>(configuration);
    services.AddHttpClient();
    services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
    services.AddSingleton<IDatabaseService, DatabaseService>();
    services.AddSingleton<ObsidianIndexer>();
    services.AddTransient<SearchService>();
    return services.BuildServiceProvider();
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string? pendingKey = null;
    foreach (var arg in args)
    {
        if (arg.StartsWith("--"))
        {
            if (pendingKey != null)
            {
                options[pendingKey] = "true"; // previous option was a flag
            }
            pendingKey = arg[2..];
        }
        else if (pendingKey != null)
        {
            options[pendingKey] = arg;
            pendingKey = null;
        }
        else
        {
            throw new ArgumentException($"Unexpected positional argument '{arg}'.");
        }
    }
    if (pendingKey != null)
    {
        options[pendingKey] = "true";
    }
    return options;
}

static string Require(Dictionary<string, string> opts, string key) =>
    opts.TryGetValue(key, out var value)
        ? value
        : throw new ArgumentException($"Missing required option --{key}.");

static void ValidateLabel(string label)
{
    if (string.IsNullOrWhiteSpace(label)
        || label is "." or ".."
        || label.IndexOfAny(['/', '\\']) >= 0
        || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
        throw new ArgumentException("--label must be a file name without path separators.");
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage:
          build-index --golden-set <path> --db <path> [--model <name>] [--user-help-folders <csv>] [--developer-docs-folders <csv>]
          validate    --golden-set <path> --db <path>
          run         --golden-set <path> --db <path> --label <label> [--output <dir>] [--depth <n>] [--model <name>]
          compare     --baseline <file> --candidate <file> [--fail-on-regression] [--epsilon <n>]
        """);
}
