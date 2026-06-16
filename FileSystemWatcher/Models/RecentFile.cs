namespace WinAgent.FileSystemWatcher.Models;

public sealed class RecentFile
{
    public string FullPath { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;

    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.UtcNow;
}
