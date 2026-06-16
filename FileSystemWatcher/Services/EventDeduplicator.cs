using System.Collections.Concurrent;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class EventDeduplicator : IDisposable
{
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<FileSystemEvent, Task> _emit;
    private readonly ConcurrentDictionary<string, FileSystemEvent> _pendingChanged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;

    public EventDeduplicator(Func<FileSystemEvent, Task> emit)
    {
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
        _timer = new Timer(_ => _ = FlushAsync(), null, FlushInterval, FlushInterval);
    }

    public async Task ProcessAsync(FileSystemEvent fileEvent)
    {
        if (fileEvent.EventType != FileSystemEventType.Changed)
        {
            await _emit(fileEvent);
            return;
        }

        _pendingChanged.AddOrUpdate(
            fileEvent.FullPath,
            fileEvent,
            (_, existing) => existing with { Timestamp = fileEvent.Timestamp });
    }

    private async Task FlushAsync()
    {
        var cutoff = DateTimeOffset.UtcNow - DeduplicationWindow;
        var flushed = new List<FileSystemEvent>();

        foreach (var kvp in _pendingChanged)
        {
            if (kvp.Value.Timestamp <= cutoff)
            {
                if (_pendingChanged.TryRemove(kvp.Key, out var removed))
                {
                    flushed.Add(removed);
                }
            }
        }

        foreach (var item in flushed)
        {
            try
            {
                await _emit(item);
            }
            catch
            {
                // Best-effort emission; downstream sinks handle retries.
            }
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _ = FlushAsync();
    }
}
