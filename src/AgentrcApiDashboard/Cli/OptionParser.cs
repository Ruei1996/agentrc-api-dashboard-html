using System.Runtime.InteropServices;

namespace AgentrcApiDashboard.Cli;

public static class OptionParser
{
    public static AppOptions Parse(string[] args)
    {
        var targetPath = string.Empty;
        var outputRoot = ResolveDefaultDownloadsPath();
        var resultDirName = "api-dashboard-result";
        string? branchOverride = null;
        string? checkoutBranch = null;
        int? intervalMinutes = null;
        var useCopilotTranslation = false;
        var copilotModel = "gpt-5";
        var maxFiles = 50000;
        var showHelp = args.Length == 0;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--target":
                    targetPath = ReadNext(args, ref i, "--target");
                    break;
                case "--output-root":
                    outputRoot = ReadNext(args, ref i, "--output-root");
                    break;
                case "--result-dir":
                    resultDirName = ReadNext(args, ref i, "--result-dir");
                    break;
                case "--branch":
                    branchOverride = ReadNext(args, ref i, "--branch");
                    break;
                case "--checkout-branch":
                    checkoutBranch = ReadNext(args, ref i, "--checkout-branch");
                    break;
                case "--interval-minutes":
                    intervalMinutes = int.Parse(ReadNext(args, ref i, "--interval-minutes"));
                    if (intervalMinutes <= 0)
                    {
                        throw new ArgumentException("--interval-minutes 必須大於 0");
                    }

                    break;
                case "--use-copilot":
                    useCopilotTranslation = true;
                    break;
                case "--copilot-model":
                    copilotModel = ReadNext(args, ref i, "--copilot-model");
                    break;
                case "--max-files":
                    maxFiles = int.Parse(ReadNext(args, ref i, "--max-files"));
                    if (maxFiles <= 0)
                    {
                        throw new ArgumentException("--max-files 必須大於 0");
                    }

                    break;
                default:
                    throw new ArgumentException($"未知參數: {args[i]}");
            }
        }

        if (!showHelp && string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("請指定 --target <path>");
        }

        return new AppOptions(
            TargetPath: string.IsNullOrWhiteSpace(targetPath) ? string.Empty : Path.GetFullPath(targetPath),
            OutputRoot: Path.GetFullPath(outputRoot),
            ResultDirName: resultDirName,
            BranchOverride: branchOverride,
            CheckoutBranch: checkoutBranch,
            IntervalMinutes: intervalMinutes,
            UseCopilotTranslation: useCopilotTranslation,
            CopilotModel: copilotModel,
            MaxFiles: maxFiles,
            ShowHelp: showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            使用方式:
              dotnet run --project src/AgentrcApiDashboard -- --target <repoPath> [options]

            必填:
              --target <path>             要掃描的專案根目錄

            常用選項:
              --output-root <path>        輸出根目錄 (預設: 使用者 Downloads)
              --result-dir <name>         輸出資料夾名稱 (預設: api-dashboard-result)
              --branch <name>             覆蓋輸出檔名中的 branch 名稱
              --checkout-branch <name>    掃描前先切換目標專案分支
              --interval-minutes <n>      每 n 分鐘自動重新掃描
              --use-copilot               啟用 Copilot SDK 生成繁中 API 說明
              --copilot-model <model>     Copilot 模型 (預設: gpt-5)
              --max-files <n>             掃描檔案上限 (預設: 50000)
              --help                      顯示說明
            """);
    }

    private static string ReadNext(IReadOnlyList<string> args, ref int index, string optionName)
    {
        var nextIndex = index + 1;
        if (nextIndex >= args.Count)
        {
            throw new ArgumentException($"{optionName} 缺少值");
        }

        index = nextIndex;
        return args[nextIndex];
    }

    private static string ResolveDefaultDownloadsPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(userProfile, "Downloads");
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Directory.GetCurrentDirectory();
        }

        return Path.Combine(home, "Downloads");
    }
}
