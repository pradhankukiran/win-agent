using System.Text.Json;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class ConsoleEventSink
{
    private readonly bool _jsonOutput;
    private readonly object _consoleLock = new();

    public ConsoleEventSink(bool jsonOutput)
    {
        _jsonOutput = jsonOutput;
    }

    public void Write(FileSystemEvent fileEvent)
    {
        if (_jsonOutput)
        {
            WriteJson(fileEvent);
        }
        else
        {
            WriteHumanReadable(fileEvent);
        }
    }

    private void WriteJson(FileSystemEvent fileEvent)
    {
        var json = JsonSerializer.Serialize(fileEvent, JsonEventSerializer.Options);
        lock (_consoleLock)
        {
            Console.WriteLine(json);
        }
    }

    private void WriteHumanReadable(FileSystemEvent fileEvent)
    {
        if (!string.IsNullOrEmpty(fileEvent.ErrorMessage))
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[{fileEvent.Timestamp:yyyy-MM-dd HH:mm:ss}] ERROR  {fileEvent.ErrorMessage}");
            }
            return;
        }

        var copyInfo = string.IsNullOrEmpty(fileEvent.PossibleCopySource)
            ? string.Empty
            : $" [COPY? source: {fileEvent.PossibleCopySource}]";

        var oldPath = string.IsNullOrEmpty(fileEvent.OldFullPath)
            ? string.Empty
            : $" (from: {fileEvent.OldFullPath})";

        lock (_consoleLock)
        {
            Console.WriteLine($"[{fileEvent.Timestamp:yyyy-MM-dd HH:mm:ss}] {fileEvent.EventType,-7} {fileEvent.Severity,-8} {fileEvent.FullPath}{oldPath}{copyInfo}");
        }
    }
}
