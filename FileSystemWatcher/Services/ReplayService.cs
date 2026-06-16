using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class ReplayService : IDisposable
{
    private static readonly TimeSpan ReplayInterval = TimeSpan.FromSeconds(10);
    private const int MaxBatchSize = 100;

    private readonly EventRepository _repository;
    private readonly BackendPushService _backendPush;
    private readonly Timer _timer;

    public ReplayService(EventRepository repository, BackendPushService backendPush)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _backendPush = backendPush ?? throw new ArgumentNullException(nameof(backendPush));
        _timer = new Timer(_ => _ = ReplayPendingAsync(), null, ReplayInterval, ReplayInterval);
    }

    private async Task ReplayPendingAsync()
    {
        try
        {
            var pending = _repository.GetPendingEvents(MaxBatchSize);

            foreach (var (id, fileEvent) in pending)
            {
                var pushed = await _backendPush.PushAsync(fileEvent);
                if (pushed)
                {
                    _repository.MarkPending(id, false);
                }
            }
        }
        catch
        {
            // Best-effort replay; leave events pending on failure.
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
