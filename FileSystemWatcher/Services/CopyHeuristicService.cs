using System.Security.Cryptography;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class CopyHeuristicService : IDisposable
{
    private readonly int _maxFiles;
    private readonly object _lock = new();
    private readonly Dictionary<string, RecentFile> _files = new(StringComparer.OrdinalIgnoreCase);

    public CopyHeuristicService(int maxFiles)
    {
        _maxFiles = maxFiles > 0 ? maxFiles : 1000;
    }

    public Task RecordExistingAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => RecordExisting(fullPath), cancellationToken);
    }

    public Task<string?> FindPossibleCopySourceAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => FindPossibleCopySource(fullPath), cancellationToken);
    }

    public void RecordExisting(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        try
        {
            var hash = ComputeHash(fullPath);
            if (hash is null)
            {
                return;
            }

            lock (_lock)
            {
                PruneIfNeeded();
                _files[fullPath] = new RecentFile
                {
                    FullPath = fullPath,
                    Hash = hash,
                    LastSeen = DateTimeOffset.UtcNow
                };
            }
        }
        catch
        {
            // Silently ignore files we cannot read (access denied, locked, etc.).
        }
    }

    public string? FindPossibleCopySource(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        try
        {
            var hash = ComputeHash(fullPath);
            if (hash is null)
            {
                return null;
            }

            lock (_lock)
            {
                foreach (var recent in _files.Values)
                {
                    if (!string.Equals(recent.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(recent.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    {
                        return recent.FullPath;
                    }
                }

                PruneIfNeeded();
                _files[fullPath] = new RecentFile
                {
                    FullPath = fullPath,
                    Hash = hash,
                    LastSeen = DateTimeOffset.UtcNow
                };
            }
        }
        catch
        {
            // Silently ignore files we cannot read.
        }

        return null;
    }

    private static string? ComputeHash(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private void PruneIfNeeded()
    {
        if (_files.Count < _maxFiles)
        {
            return;
        }

        var oldest = _files.Values.OrderBy(f => f.LastSeen).Take(_files.Count - _maxFiles + 1);
        foreach (var item in oldest)
        {
            _files.Remove(item.FullPath);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _files.Clear();
        }
    }
}
