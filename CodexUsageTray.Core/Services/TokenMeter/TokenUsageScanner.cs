using CodexUsageTray.Core.Models;

namespace CodexUsageTray.Core.Services.TokenMeter;

public sealed class TokenUsageScanner
{
    private readonly TokenUsageFileParser _parser;
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TokenUsageScanner()
        : this(new TokenUsageFileParser())
    {
    }

    public TokenUsageScanner(TokenUsageFileParser parser)
    {
        _parser = parser;
    }

    public Task<TokenUsageScanResult> ScanAsync(TokenUsageScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TokenUsageScanOptions();
        return Task.Run(() => Scan(options, cancellationToken), cancellationToken);
    }

    private TokenUsageScanResult Scan(TokenUsageScanOptions options, CancellationToken cancellationToken)
    {
        List<TokenUsageEntry> entries = [];
        int scannedFiles = 0;
        int failedFiles = 0;

        foreach (TokenClientDefinition client in TokenClientCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string path in EnumerateClientFiles(client, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedFiles++;

                FileInfo file = new(path);
                TokenFileFingerprint fingerprint = TokenFileFingerprint.FromFile(file);
                if (_cache.TryGetValue(path, out CachedFile? cached) && cached.Fingerprint == fingerprint)
                {
                    entries.AddRange(cached.Entries);
                    if (!cached.Succeeded)
                    {
                        failedFiles++;
                    }

                    continue;
                }

                TokenFileParseResult result = _parser.ParseFile(client, path);
                _cache[path] = new CachedFile(fingerprint, result.Entries, result.Succeeded);
                entries.AddRange(result.Entries);
                if (!result.Succeeded)
                {
                    failedFiles++;
                }
            }
        }

        return new TokenUsageScanResult(entries, scannedFiles, failedFiles);
    }

    private static IEnumerable<string> EnumerateClientFiles(TokenClientDefinition client, TokenUsageScanOptions options)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string configuredPath in options.ResolveClientDirectories(client))
        {
            foreach (string path in EnumerateConfiguredPath(client, configuredPath))
            {
                if (seen.Add(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateConfiguredPath(TokenClientDefinition client, string configuredPath)
    {
        if (File.Exists(configuredPath))
        {
            if (FileMatches(configuredPath, client.Pattern))
            {
                yield return configuredPath;
            }

            yield break;
        }

        string directory = configuredPath;
        if (!Directory.Exists(directory))
        {
            directory = Path.GetDirectoryName(configuredPath) ?? configuredPath;
        }

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, client.Pattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (string file in files)
        {
            yield return file;
        }
    }

    private static bool FileMatches(string path, string pattern)
    {
        string fileName = Path.GetFileName(path);
        if (string.Equals(pattern, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.StartsWith('*') && fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.EndsWith('*') && fileName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            string[] parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
            int index = 0;
            foreach (string part in parts)
            {
                int found = fileName.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                index = found + part.Length;
            }

            return true;
        }

        return false;
    }

    private sealed record CachedFile(TokenFileFingerprint Fingerprint, IReadOnlyList<TokenUsageEntry> Entries, bool Succeeded);
}
