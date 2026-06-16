using System.Collections.Concurrent;
using WinAgent.FileSystemWatcher.Configuration;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class WatcherService : IDisposable
{
    private readonly WatcherOptions _options;
    private readonly Func<FileSystemEvent, CancellationToken, Task> _onEvent;
    private readonly ConcurrentDictionary<string, System.IO.FileSystemWatcher> _watchers = new();

    public WatcherService(WatcherOptions options, Func<FileSystemEvent, CancellationToken, Task> onEvent)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
    }

    public bool Start()
    {
        var anyStarted = false;

        foreach (var path in _options.WatchedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    EmitError($"Directory not found: {path}");
                    continue;
                }

                var watcher = new System.IO.FileSystemWatcher(path)
                {
                    IncludeSubdirectories = _options.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.Size
                        | NotifyFilters.CreationTime
                };

                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;
                watcher.Renamed += OnRenamed;
                watcher.Changed += OnChanged;
                watcher.Error += OnError;

                watcher.EnableRaisingEvents = true;
                _watchers[path] = watcher;
                anyStarted = true;

                Console.WriteLine($"[Watcher] Started watching: {path}");
            }
            catch (UnauthorizedAccessException)
            {
                EmitError($"Access denied: {path}");
            }
            catch (Exception ex)
            {
                EmitError($"Failed to watch {path}: {ex.Message}");
            }
        }

        return anyStarted;
    }

    public void Stop()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () => await _onEvent(new FileSystemEvent
        {
            EventType = FileSystemEventType.Created,
            FullPath = e.FullPath
        }, CancellationToken.None));
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () => await _onEvent(new FileSystemEvent
        {
            EventType = FileSystemEventType.Deleted,
            FullPath = e.FullPath
        }, CancellationToken.None));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _ = Task.Run(async () => await _onEvent(new FileSystemEvent
        {
            EventType = FileSystemEventType.Renamed,
            FullPath = e.FullPath,
            OldFullPath = e.OldFullPath
        }, CancellationToken.None));
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () => await _onEvent(new FileSystemEvent
        {
            EventType = FileSystemEventType.Changed,
            FullPath = e.FullPath
        }, CancellationToken.None));
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        EmitError($"Watcher error: {ex.Message}");
    }

    private void EmitError(string message)
    {
        _ = Task.Run(async () => await _onEvent(new FileSystemEvent
        {
            EventType = FileSystemEventType.Changed,
            FullPath = string.Empty,
            ErrorMessage = message
        }, CancellationToken.None));
    }
}
