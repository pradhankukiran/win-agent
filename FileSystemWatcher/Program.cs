using Microsoft.Extensions.Configuration;
using WinAgent.FileSystemWatcher.Configuration;
using WinAgent.FileSystemWatcher.Models;
using WinAgent.FileSystemWatcher.Services;

var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Directory.GetCurrentDirectory();

var cli = ParseCommandLineArgs(args);

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(basePath);

if (!string.IsNullOrEmpty(cli.ConfigPath))
{
    configBuilder.AddJsonFile(cli.ConfigPath, optional: false, reloadOnChange: true);
}
else
{
    configBuilder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
}

var configuration = configBuilder.Build();

var options = new WatcherOptions();
configuration.GetSection("Watcher").Bind(options);
configuration.GetSection("StatusApi").Bind(options);

// Apply CLI overrides.
if (cli.WatchedPaths.Count > 0)
{
    options.WatchedPaths = cli.WatchedPaths;
}

if (cli.JsonOutput.HasValue)
{
    options.JsonOutput = cli.JsonOutput.Value;
}

if (cli.Port.HasValue)
{
    options.Port = cli.Port.Value;
}

if (options.WatchedPaths.Count == 0)
{
    Console.WriteLine("No directories configured for watching. Update appsettings.json -> Watcher:WatchedPaths or use --paths.");
    return 1;
}

Console.WriteLine("WinAgent.FileSystemWatcher starting...");
Console.WriteLine($"Output mode: {(options.JsonOutput ? "JSON" : "human-readable")}");
Console.WriteLine($"Status API: http://localhost:{options.Port}");
Console.WriteLine("Press Ctrl+C to exit.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var startedAt = DateTimeOffset.UtcNow;
var copyHeuristic = new CopyHeuristicService(options.CopyHeuristicMaxFiles);
var filterService = new FilterService(options);
var severityService = new SeverityService(options);
var consoleSink = new ConsoleEventSink(options.JsonOutput);
var repository = new EventRepository(basePath);
var backendPush = new BackendPushService();
var broadcaster = new EventBroadcaster();
var replayService = new ReplayService(repository, backendPush);

EventBatcher batcher = null!;
batcher = new EventBatcher(
    options.BatchSize,
    TimeSpan.FromSeconds(options.BatchFlushIntervalSeconds),
    async (batch, cancellationToken) =>
    {
        foreach (var fileEvent in batch)
        {
            consoleSink.Write(fileEvent);
            broadcaster.Broadcast(fileEvent);

            var pending = true;
            try
            {
                pending = !await backendPush.PushAsync(fileEvent, cancellationToken);
            }
            catch
            {
                pending = true;
            }

            repository.Insert(fileEvent, pending);
        }
    });

using var pipeline = new EventPipeline(options, filterService, copyHeuristic, severityService, batcher);
using var watcherService = new WatcherService(options, pipeline.ProcessEventAsync);
using var statusApi = new StatusApi(options, repository, broadcaster, startedAt);

if (!watcherService.Start())
{
    Console.WriteLine("No watchers could be started. Check configured paths and permissions.");
    return 1;
}

await statusApi.StartAsync(cts.Token);

foreach (var path in options.WatchedPaths)
{
    if (options.EnableCopyHeuristic && Directory.Exists(path))
    {
        _ = Task.Run(async () => await SeedExistingFilesAsync(copyHeuristic, path, options.IncludeSubdirectories, cts.Token), cts.Token);
    }
}

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Expected on shutdown.
}

Console.WriteLine("Shutting down...");

watcherService.Stop();
await statusApi.StopAsync(CancellationToken.None);
batcher.Dispose();
pipeline.Dispose();
replayService.Dispose();
broadcaster.Complete();
repository.Dispose();

return 0;

static async Task SeedExistingFilesAsync(CopyHeuristicService copyHeuristic, string rootPath, bool recursive, CancellationToken cancellationToken)
{
    try
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true
        };

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", enumerationOptions))
        {
            await copyHeuristic.RecordExistingAsync(file, cancellationToken);
        }
    }
    catch
    {
        // Best-effort seeding; ignore inaccessible directories.
    }
}

static CliArgs ParseCommandLineArgs(string[] args)
{
    var result = new CliArgs();

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        switch (arg)
        {
            case "--config" when i + 1 < args.Length:
                result.ConfigPath = args[++i];
                break;
            case "--paths" when i + 1 < args.Length:
                result.WatchedPaths = args[++i].Split(';', StringSplitOptions.RemoveEmptyEntries);
                break;
            case "--json":
                result.JsonOutput = true;
                break;
            case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out var port):
                result.Port = port;
                break;
            case "--help":
                PrintHelp();
                Environment.Exit(0);
                break;
        }
    }

    return result;
}

static void PrintHelp()
{
    Console.WriteLine("WinAgent.FileSystemWatcher");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --config <path>           Path to appsettings.json");
    Console.WriteLine("  --paths <path1>;<path2>   Override watched paths (semicolon-separated)");
    Console.WriteLine("  --json                    Enable JSON log output to console");
    Console.WriteLine("  --port <number>           HTTP status endpoint port (default 5001)");
    Console.WriteLine("  --help                    Show this help");
}

internal sealed class CliArgs
{
    public string? ConfigPath { get; set; }

    public IReadOnlyList<string> WatchedPaths { get; set; } = Array.Empty<string>();

    public bool? JsonOutput { get; set; }

    public int? Port { get; set; }
}
