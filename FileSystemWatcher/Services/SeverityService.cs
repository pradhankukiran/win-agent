using System.Collections.Concurrent;
using WinAgent.FileSystemWatcher.Configuration;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class SeverityService
{
    private const int BurstThreshold = 10;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan UsbActivityWindow = TimeSpan.FromSeconds(30);

    private readonly WatcherOptions _options;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _eventsByPath = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _usbActivityExpires = DateTimeOffset.MinValue;

    public SeverityService(WatcherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void ReportUsbActivity()
    {
        _usbActivityExpires = DateTimeOffset.UtcNow + UsbActivityWindow;
    }

    public EventSeverity AssignSeverity(FileSystemEvent fileEvent)
    {
        if (!string.IsNullOrEmpty(fileEvent.ErrorMessage))
        {
            return EventSeverity.Low;
        }

        // USB connected + file activity -> Critical (stub rule; call ReportUsbActivity to trigger).
        if (DateTimeOffset.UtcNow <= _usbActivityExpires)
        {
            return EventSeverity.Critical;
        }

        // Activity outside configured business hours -> High.
        if (!IsWithinBusinessHours(fileEvent.Timestamp))
        {
            return EventSeverity.High;
        }

        // Rapid burst (>10 events in 1 second from same path) -> High.
        if (IsBurst(fileEvent.FullPath, fileEvent.Timestamp))
        {
            return EventSeverity.High;
        }

        // Copy heuristic match -> Medium.
        if (!string.IsNullOrEmpty(fileEvent.PossibleCopySource))
        {
            return EventSeverity.Medium;
        }

        return EventSeverity.Low;
    }

    private bool IsWithinBusinessHours(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        var start = ParseTime(_options.BusinessHours.Start, new TimeSpan(9, 0, 0));
        var end = ParseTime(_options.BusinessHours.End, new TimeSpan(18, 0, 0));

        var timeOfDay = local.TimeOfDay;
        return timeOfDay >= start && timeOfDay <= end;
    }

    private static TimeSpan ParseTime(string value, TimeSpan fallback)
    {
        return TimeSpan.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private bool IsBurst(string fullPath, DateTimeOffset timestamp)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return false;
        }

        var queue = _eventsByPath.GetOrAdd(fullPath, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            var cutoff = timestamp - BurstWindow;
            while (queue.Count > 0 && queue.Peek() < cutoff)
            {
                queue.Dequeue();
            }

            queue.Enqueue(timestamp);
            return queue.Count > BurstThreshold;
        }
    }
}
