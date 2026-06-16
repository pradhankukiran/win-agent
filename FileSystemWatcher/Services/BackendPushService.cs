using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class BackendPushService
{
    // PoC backend push: always succeeds. Swap this for a real HTTP/queue push.
    public Task<bool> PushAsync(FileSystemEvent fileEvent, CancellationToken cancellationToken = default)
    {
        // Simulate an unreliable backend by randomly failing ~10% of the time
        // so the SQLite retry loop can be exercised.
        var success = Random.Shared.NextDouble() > 0.1;
        return Task.FromResult(success);
    }
}
