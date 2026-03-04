using AgentrcApiDashboard.Cli;
using AgentrcApiDashboard.Services;

namespace AgentrcApiDashboard;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AppOptions options;
        try
        {
            options = OptionParser.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"參數錯誤: {ex.Message}");
            OptionParser.PrintHelp();
            return 1;
        }

        if (options.ShowHelp)
        {
            OptionParser.PrintHelp();
            return 0;
        }

        if (!Directory.Exists(options.TargetPath))
        {
            Console.Error.WriteLine($"目標路徑不存在: {options.TargetPath}");
            return 1;
        }

        var gitService = new GitService();
        var gitIgnoreService = new GitIgnoreService(gitService);
        var translator = new CopilotTranslationService();
        var swaggerParser = new SwaggerParser(translator);
        var scanner = new RepoScanner(gitService, gitIgnoreService, swaggerParser);
        var renderer = new DashboardRenderer();
        var outputService = new OutputService();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("已收到中斷訊號，準備停止...");
        };

        if (!string.IsNullOrWhiteSpace(options.CheckoutBranch))
        {
            var checkoutResult = await gitService.CheckoutBranchAsync(options.TargetPath, options.CheckoutBranch!, cts.Token);
            if (!checkoutResult.Success)
            {
                Console.Error.WriteLine($"切換分支失敗: {checkoutResult.Stderr}");
                return 1;
            }
        }

        do
        {
            var metadata = await scanner.ScanAsync(options, cts.Token);
            var html = renderer.Render(metadata);
            var output = outputService.WriteOutputs(metadata, html, options);

            Console.WriteLine($"掃描完成: {metadata.Project.Name} ({metadata.Project.Branch})");
            Console.WriteLine($"JSON: {output.JsonPath}");
            Console.WriteLine($"HTML: {output.HtmlPath}");

            if (metadata.Warnings.Count > 0)
            {
                Console.WriteLine("警告:");
                foreach (var warning in metadata.Warnings)
                {
                    Console.WriteLine($"- {warning}");
                }
            }

            if (!options.IntervalMinutes.HasValue)
            {
                break;
            }

            var delay = TimeSpan.FromMinutes(options.IntervalMinutes.Value);
            Console.WriteLine($"將於 {delay.TotalMinutes:0} 分鐘後重新掃描，按 Ctrl+C 停止。");

            try
            {
                await Task.Delay(delay, cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        } while (!cts.Token.IsCancellationRequested);

        return 0;
    }
}
