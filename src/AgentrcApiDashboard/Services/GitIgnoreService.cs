using System.Text;
using System.Text.RegularExpressions;

namespace AgentrcApiDashboard.Services;

public sealed class GitIgnoreService
{
    private readonly GitService _gitService;

    public GitIgnoreService(GitService gitService)
    {
        _gitService = gitService;
    }

    public async Task<HashSet<string>> ResolveIgnoredFilesAsync(
        string rootPath,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var normalized = relativePaths
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var (gitSuccess, ignoredByGit) = await ResolveWithGitAsync(rootPath, normalized, cancellationToken);
        if (gitSuccess)
        {
            return ignoredByGit;
        }

        return ResolveWithFallback(rootPath, normalized);
    }

    private async Task<(bool Success, HashSet<string> Ignored)> ResolveWithGitAsync(
        string rootPath,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var stdin = string.Join('\n', relativePaths);
        var result = await _gitService.RunGitAsync(rootPath, "check-ignore --stdin", stdin, cancellationToken, timeoutSeconds: 180);

        if (result.ExitCode is 0 or 1)
        {
            var ignored = result.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return (true, ignored);
        }

        return (false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static HashSet<string> ResolveWithFallback(string rootPath, IReadOnlyList<string> relativePaths)
    {
        var gitIgnorePath = Path.Combine(rootPath, ".gitignore");
        if (!File.Exists(gitIgnorePath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var patterns = ParsePatterns(File.ReadLines(gitIgnorePath));
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in relativePaths)
        {
            var isIgnored = false;
            foreach (var pattern in patterns)
            {
                if (!pattern.Regex.IsMatch(relativePath))
                {
                    continue;
                }

                isIgnored = !pattern.IsNegation;
            }

            if (isIgnored)
            {
                ignored.Add(relativePath);
            }
        }

        return ignored;
    }

    private static IReadOnlyList<IgnorePattern> ParsePatterns(IEnumerable<string> lines)
    {
        var patterns = new List<IgnorePattern>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var isNegation = line.StartsWith('!');
            if (isNegation)
            {
                line = line[1..];
            }

            if (line.StartsWith('\\') && line.Length > 1)
            {
                line = line[1..];
            }

            if (line.EndsWith('/'))
            {
                line += "**";
            }

            var anchored = line.StartsWith('/');
            if (anchored)
            {
                line = line[1..];
            }
            else if (!line.Contains('/'))
            {
                line = $"**/{line}";
            }

            patterns.Add(new IgnorePattern(new Regex(GlobToRegex(line), RegexOptions.Compiled), isNegation));
        }

        return patterns;
    }

    private static string GlobToRegex(string glob)
    {
        var normalized = NormalizePath(glob);
        var sb = new StringBuilder("^");
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (c == '*')
            {
                if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++;
                }
                else
                {
                    sb.Append("[^/]*");
                }

                continue;
            }

            if (c == '?')
            {
                sb.Append("[^/]");
                continue;
            }

            sb.Append(Regex.Escape(c.ToString()));
        }

        sb.Append('$');
        return sb.ToString();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record IgnorePattern(Regex Regex, bool IsNegation);
}
