using System.Threading.Channels;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class EventBroadcaster
{
    private readonly Channel<FileSystemEvent> _channel = Channel.CreateUnbounded<FileSystemEvent>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = true
    });

    public void Broadcast(FileSystemEvent fileEvent)
    {
        _channel.Writer.TryWrite(fileEvent);
    }

    public IAsyncEnumerable<FileSystemEvent> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Complete()
    {
        _channel.Writer.Complete();
    }
}
