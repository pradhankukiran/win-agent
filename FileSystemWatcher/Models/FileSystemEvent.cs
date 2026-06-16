namespace WinAgent.FileSystemWatcher.Models;

public enum FileSystemEventType
{
    Created,
    Deleted,
    Renamed,
    Changed
}

public enum EventSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record FileSystemEvent
{
    public FileSystemEventType EventType { get; init; }

    public string FullPath { get; init; } = string.Empty;

    public string? OldFullPath { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? PossibleCopySource { get; init; }

    public string? ErrorMessage { get; init; }

    public EventSeverity Severity { get; init; } = EventSeverity.Low;

    public bool Pending { get; init; }
}
