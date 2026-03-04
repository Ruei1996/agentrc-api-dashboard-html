using System.Diagnostics;

namespace AgentrcApiDashboard.Services;

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken,
        int timeoutSeconds = 60)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult(false, -1, string.Empty, $"無法啟動程序: {fileName}");
            }

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new ProcessResult(process.ExitCode == 0, process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return new ProcessResult(false, -1, string.Empty, $"程序逾時或被取消: {fileName} {arguments}");
        }
        catch (Exception ex)
        {
            return new ProcessResult(false, -1, string.Empty, ex.Message);
        }
        finally
        {
            process.Dispose();
        }
    }
}

public sealed record ProcessResult(
    bool Success,
    int ExitCode,
    string Stdout,
    string Stderr
);
