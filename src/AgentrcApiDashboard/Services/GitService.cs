namespace AgentrcApiDashboard.Services;

public sealed class GitService
{
    public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repoPath, "rev-parse --abbrev-ref HEAD", null, cancellationToken);
        if (!result.Success)
        {
            return "unknown";
        }

        var branch = result.Stdout.Trim();
        return string.IsNullOrWhiteSpace(branch) ? "unknown" : branch;
    }

    public async Task<DateTimeOffset?> GetLastCommitTimeAsync(string repoPath, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repoPath, "log -1 --format=%cI", null, cancellationToken);
        if (!result.Success)
        {
            return null;
        }

        var raw = result.Stdout.Trim();
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    public async Task<ProcessResult> CheckoutBranchAsync(string repoPath, string branch, CancellationToken cancellationToken)
    {
        var safeBranch = EscapeArg(branch);
        var switchResult = await RunGitAsync(repoPath, $"switch {safeBranch}", null, cancellationToken);
        if (switchResult.Success)
        {
            return switchResult;
        }

        return await RunGitAsync(repoPath, $"checkout {safeBranch}", null, cancellationToken);
    }

    public Task<ProcessResult> RunGitAsync(
        string repoPath,
        string args,
        string? standardInput,
        CancellationToken cancellationToken,
        int timeoutSeconds = 120)
    {
        var arguments = $"-C {EscapeArg(repoPath)} {args}";
        return ProcessRunner.RunAsync("git", arguments, repoPath, standardInput, cancellationToken, timeoutSeconds);
    }

    private static string EscapeArg(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
