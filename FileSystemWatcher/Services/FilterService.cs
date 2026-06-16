using Microsoft.Extensions.FileSystemGlobbing;
using WinAgent.FileSystemWatcher.Configuration;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class FilterService
{
    private readonly WatcherOptions _options;
    private readonly Matcher _ignoreMatcher;
    private readonly HashSet<string> _includeExtensions;
    private readonly HashSet<string> _excludeExtensions;

    public FilterService(WatcherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _includeExtensions = new HashSet<string>(
            options.IncludeExtensions.Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        _excludeExtensions = new HashSet<string>(
            options.ExcludeExtensions.Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        _ignoreMatcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        if (options.IgnorePatterns.Count > 0)
        {
            _ignoreMatcher.AddIncludePatterns(options.IgnorePatterns);
        }
    }

    public bool ShouldInclude(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var extension = NormalizeExtension(Path.GetExtension(fullPath));

        if (_includeExtensions.Count > 0 && !_includeExtensions.Contains(extension))
        {
            return false;
        }

        if (_excludeExtensions.Count > 0 && _excludeExtensions.Contains(extension))
        {
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        if (!string.IsNullOrEmpty(fileName) && _ignoreMatcher.Match(fileName).HasMatches)
        {
            return false;
        }

        return true;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith('.') ? extension : $".{extension}";
    }
}
