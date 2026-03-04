namespace AgentrcApiDashboard.Cli;

public sealed record AppOptions(
    string TargetPath,
    string OutputRoot,
    string ResultDirName,
    string? BranchOverride,
    string? CheckoutBranch,
    int? IntervalMinutes,
    bool UseCopilotTranslation,
    string CopilotModel,
    int MaxFiles,
    bool ShowHelp
);
