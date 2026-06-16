using WinAgent.FileSystemWatcher.Configuration;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class EventPipeline : IDisposable
{
    private readonly WatcherOptions _options;
    private readonly FilterService _filterService;
    private readonly CopyHeuristicService _copyHeuristic;
    private readonly SeverityService _severityService;
    private readonly EventBatcher _batcher;
    private readonly EventDeduplicator _deduplicator;

    public EventPipeline(
        WatcherOptions options,
        FilterService filterService,
        CopyHeuristicService copyHeuristic,
        SeverityService severityService,
        EventBatcher batcher)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _copyHeuristic = copyHeuristic ?? throw new ArgumentNullException(nameof(copyHeuristic));
        _severityService = severityService ?? throw new ArgumentNullException(nameof(severityService));
        _batcher = batcher ?? throw new ArgumentNullException(nameof(batcher));

        _deduplicator = new EventDeduplicator(ProcessDedupedEventAsync);
    }

    public async Task ProcessEventAsync(FileSystemEvent fileEvent, CancellationToken cancellationToken)
    {
        if (!_filterService.ShouldInclude(fileEvent.FullPath))
        {
            return;
        }

        await _deduplicator.ProcessAsync(fileEvent);
    }

    private async Task ProcessDedupedEventAsync(FileSystemEvent fileEvent)
    {
        fileEvent = await EnrichWithCopyHeuristicAsync(fileEvent);
        var severity = _severityService.AssignSeverity(fileEvent);
        fileEvent = fileEvent with { Severity = severity };
        _batcher.Enqueue(fileEvent);
    }

    private async Task<FileSystemEvent> EnrichWithCopyHeuristicAsync(FileSystemEvent fileEvent)
    {
        if (!_options.EnableCopyHeuristic || !File.Exists(fileEvent.FullPath))
        {
            return fileEvent;
        }

        if (fileEvent.EventType == FileSystemEventType.Created)
        {
            var source = await _copyHeuristic.FindPossibleCopySourceAsync(fileEvent.FullPath);
            return fileEvent with { PossibleCopySource = source };
        }

        if (fileEvent.EventType == FileSystemEventType.Changed)
        {
            await _copyHeuristic.RecordExistingAsync(fileEvent.FullPath);
        }

        return fileEvent;
    }

    public void Dispose()
    {
        _deduplicator.Dispose();
    }
}
