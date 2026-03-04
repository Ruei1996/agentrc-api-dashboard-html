namespace AgentrcApiDashboard.Models;

public sealed record DashboardMetadata(
    ProjectInfo Project,
    ScanSettings Settings,
    ScanStats Stats,
    MandatoryFileStatus MandatoryFiles,
    EnvInfo EnvironmentVariables,
    CopilotInstructionsInfo CopilotInstructions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LanguageInfo> Languages,
    IReadOnlyList<DependencyInfo> Dependencies,
    IReadOnlyList<RouteInfo> Routes,
    IReadOnlyList<string> SqlFiles,
    IReadOnlyList<ApiSpecInfo> ApiSpecs,
    IReadOnlyList<CiWorkflowInfo> CiWorkflows,
    BuildRunGuide BuildGuide,
    TreeNode FileTree
);

public sealed record ProjectInfo(
    string Name,
    string Path,
    string Branch,
    string LastUpdatedUtc,
    string GeneratedAtUtc,
    string Description
);

public sealed record ScanSettings(
    string OutputRoot,
    string ResultDirName,
    bool UseCopilotTranslation,
    string CopilotModel
);

public sealed record ScanStats(
    int TotalFiles,
    int IgnoredFiles,
    int ScannedFiles,
    int TotalApiEndpoints,
    int TotalRoutes
);

public sealed record MandatoryFileStatus(
    IReadOnlyList<string> Found,
    IReadOnlyList<string> Missing
);

public sealed record EnvInfo(
    bool Exists,
    IReadOnlyList<string> Keys
);

public sealed record CopilotInstructionsInfo(
    bool Exists,
    string RelativePath,
    string Summary
);

public sealed record LanguageInfo(string Name, int FileCount);

public sealed record DependencyInfo(
    string Ecosystem,
    string Name,
    string Version,
    string SourceFile
);

public sealed record RouteInfo(
    string Method,
    string Path,
    string SourceFile
);

public sealed record ApiSpecInfo(
    string RelativePath,
    IReadOnlyList<ApiEndpointInfo> Endpoints
);

public sealed record ApiEndpointInfo(
    string Method,
    string Path,
    string Summary,
    string Description,
    string TraditionalChineseUsage,
    string SourceHint,
    IReadOnlyList<ApiParameterInfo> Parameters,
    IReadOnlyList<ApiResponseInfo> Responses,
    IReadOnlyList<string> FlowSteps
);

public sealed record ApiParameterInfo(
    string Name,
    string In,
    bool Required,
    string Type,
    string Description
);

public sealed record ApiResponseInfo(
    string StatusCode,
    string Description,
    string SchemaType
);

public sealed record CiWorkflowInfo(
    string Name,
    string RelativePath,
    string Trigger
);

public sealed record BuildRunGuide(
    IReadOnlyList<string> BuildCommands,
    IReadOnlyList<string> RunCommands,
    IReadOnlyList<string> TestCommands,
    IReadOnlyList<string> ResourceLinks
);

public sealed record TreeNode(
    string Name,
    string NodeType,
    IReadOnlyList<TreeNode> Children
);

public sealed record RepoFile(
    string RelativePath,
    string FullPath,
    long SizeBytes
);

public sealed record OutputArtifactPaths(string JsonPath, string HtmlPath);

public sealed record ApiTranslationRequest(
    string Key,
    string Method,
    string Path,
    string Summary,
    string Description
);

public sealed record ApiTranslationResult(
    string UsageZh,
    IReadOnlyList<string> FlowStepsZh
);
