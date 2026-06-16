namespace WinAgent.FileSystemWatcher.Configuration;

public sealed class BusinessHoursOptions
{
    public string Start { get; set; } = "09:00";

    public string End { get; set; } = "18:00";
}

public sealed class WatcherOptions
{
    public IReadOnlyList<string> WatchedPaths { get; set; } = Array.Empty<string>();

    public bool IncludeSubdirectories { get; set; } = true;

    public bool EnableCopyHeuristic { get; set; } = true;

    public int BatchFlushIntervalSeconds { get; set; } = 2;

    public int BatchSize { get; set; } = 50;

    public int CopyHeuristicMaxFiles { get; set; } = 1000;

    public IReadOnlyList<string> IncludeExtensions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ExcludeExtensions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> IgnorePatterns { get; set; } = Array.Empty<string>();

    public BusinessHoursOptions BusinessHours { get; set; } = new();

    public bool JsonOutput { get; set; }

    public int Port { get; set; } = 5001;
}
