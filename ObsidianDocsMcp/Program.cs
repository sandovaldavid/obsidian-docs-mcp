using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ObsidianDocsMcp.Services;
using ObsidianDocsMcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

// Send logs to Stderr, since Stdout is reserved for the MCP protocol communication (JSON-RPC).
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Load configuration from appsettings.json.
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// Register HttpClient and business services in the DI container.
builder.Services.AddHttpClient(); // HttpClient for ZIP downloads
builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<ObsidianIndexer>();

// Register the tools class in DI so the MCP SDK can instantiate it with its dependencies.
builder.Services.AddTransient<ObsidianSearchTools>();

// Configure the MCP server with the standard input/output (stdio) transport.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ObsidianSearchTools>();

var host = builder.Build();

// Initialize the database asynchronously at host startup.
using (var scope = host.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await dbService.InitializeDatabaseAsync();
        logger.LogInformation("Database verified and ready at startup.");
    }
    catch (System.Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database during startup.");
    }
}

if (args.Length > 0)
{
    using var scope = host.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (args[0] == "index")
    {
        var indexer = scope.ServiceProvider.GetRequiredService<ObsidianIndexer>();
        // Optional positional args: `index [userHelpFolders] [developerDocsFolders]`,
        // e.g. `index en,es,Sandbox` to restrict User Help to those top-level folders.
        // Omitted (or run with no args at all) indexes everything, unchanged from before.
        string? userHelpFolders = args.Length > 1 ? args[1] : null;
        string? developerDocsFolders = args.Length > 2 ? args[2] : null;
        logger.LogInformation("Starting manual CLI indexation...");
        await indexer.IndexAllDocsAsync(userHelpFolders, developerDocsFolders);
        logger.LogInformation("CLI indexation complete. Exiting.");
        return;
    }
    else if (args[0] == "setup")
    {
        logger.LogInformation("Starting autoconfiguration for Claude Desktop...");
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configDir = "";

            if (OperatingSystem.IsWindows())
            {
                configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            }
            else if (OperatingSystem.IsMacOS())
            {
                configDir = Path.Combine(home, "Library", "Application Support", "Claude");
            }
            else // Linux / WSL
            {
                configDir = Path.Combine(home, ".config", "Claude");
            }

            string configFile = Path.Combine(configDir, "claude_desktop_config.json");
            if (Directory.Exists(configDir))
            {
                var configData = new Dictionary<string, object>();
                if (File.Exists(configFile))
                {
                    try
                    {
                        var json = File.ReadAllText(configFile);
                        configData = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
                    }
                    catch { }
                }

                var servers = new Dictionary<string, object>();
                if (configData.TryGetValue("mcpServers", out var existingServersObj))
                {
                    try
                    {
                        var tempJson = JsonSerializer.Serialize(existingServersObj);
                        servers = JsonSerializer.Deserialize<Dictionary<string, object>>(tempJson) ?? new();
                    }
                    catch { }
                }

                // Global tool configuration in Claude Desktop
                servers["obsidian-docs-mcp"] = new { command = "obsidian-docs-mcp" };
                configData["mcpServers"] = servers;

                var finalJson = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(configFile, finalJson);
                logger.LogInformation("Successfully configured obsidian-docs-mcp command in Claude Desktop!");
                logger.LogInformation("Updated file: {Path}", configFile);
            }
            else
            {
                logger.LogWarning("Claude Desktop configuration directory not found at: {Dir}", configDir);
                logger.LogInformation("You can manually add the following fragment to your configuration:");
                Console.WriteLine("\n\"obsidian-docs-mcp\": { \"command\": \"obsidian-docs-mcp\" }\n");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during autoconfiguration.");
        }
        return;
    }
    else if (args[0] == "search" && args.Length > 1)
    {
        var searchTools = scope.ServiceProvider.GetRequiredService<ObsidianSearchTools>();
        var query = string.Join(" ", args.Skip(1));
        logger.LogInformation("Starting manual CLI search for: '{Query}'", query);
        var results = await searchTools.SearchDocumentation(query, limit: 3);
        Console.WriteLine("\n=== Search Results ===");
        Console.WriteLine(results);
        Console.WriteLine("======================\n");
        return;
    }
    else if (args[0] == "index-status")
    {
        var searchTools = scope.ServiceProvider.GetRequiredService<ObsidianSearchTools>();
        var status = await searchTools.IndexStatus();
        Console.WriteLine("\n=== Index Status ===");
        Console.WriteLine(status);
        Console.WriteLine("=====================\n");
        return;
    }
}

// If the index is empty (first run / fresh install), try the prebuilt index first (fast download,
// no local embedding compute) and only fall back to a live background reindex if that's
// unavailable, so the MCP server doesn't sit idle returning "no results" in the meantime.
using (var scope = host.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    if (await dbService.GetTotalChunksCountAsync() == 0)
    {
        var indexer = scope.ServiceProvider.GetRequiredService<ObsidianIndexer>();
        if (await indexer.TryDownloadPrebuiltIndexAsync())
        {
            logger.LogInformation("Downloaded prebuilt documentation index.");
        }
        else
        {
            logger.LogInformation("No prebuilt index available — triggering automatic first-time indexation in the background.");
            var searchTools = scope.ServiceProvider.GetRequiredService<ObsidianSearchTools>();
            logger.LogInformation("{Result}", await searchTools.ReindexDocumentation());
        }
    }
}

await host.RunAsync();
