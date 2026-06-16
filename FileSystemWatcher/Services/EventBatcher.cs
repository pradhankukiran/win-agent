using System.Collections.Concurrent;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class EventBatcher : IDisposable
{
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly Func<IReadOnlyList<FileSystemEvent>, CancellationToken, Task> _flushAction;
    private readonly ConcurrentQueue<FileSystemEvent> _queue = new();
    private readonly Timer _timer;
    private readonly object _flushLock = new();

    public EventBatcher(
        int batchSize,
        TimeSpan flushInterval,
        Func<IReadOnlyList<FileSystemEvent>, CancellationToken, Task> flushAction)
    {
        _batchSize = batchSize > 0 ? batchSize : 50;
        _flushInterval = flushInterval > TimeSpan.Zero ? flushInterval : TimeSpan.FromSeconds(2);
        _flushAction = flushAction ?? throw new ArgumentNullException(nameof(flushAction));
        _timer = new Timer(_ => _ = FlushAsync(CancellationToken.None), null, _flushInterval, _flushInterval);
    }

    public void Enqueue(FileSystemEvent fileEvent)
    {
        _queue.Enqueue(fileEvent);

        if (_queue.Count >= _batchSize)
        {
            _ = FlushAsync(CancellationToken.None);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        lock (_flushLock)
        {
            var batch = new List<FileSystemEvent>(_queue.Count);
            while (_queue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count == 0)
            {
                return;
            }

            _ = Task.Run(async () => await _flushAction(batch, cancellationToken), cancellationToken);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _ = FlushAsync(CancellationToken.None);
    }
}
