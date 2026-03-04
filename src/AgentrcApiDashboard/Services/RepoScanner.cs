using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgentrcApiDashboard.Cli;
using AgentrcApiDashboard.Models;

namespace AgentrcApiDashboard.Services;

public sealed partial class RepoScanner
{
    private static readonly HashSet<string> RouteFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".go",
        ".cs",
        ".js",
        ".ts",
        ".tsx",
        ".py",
        ".java"
    };

    private static readonly string[] MandatoryRelativePaths =
    [
        ".env",
        ".gitignore",
        ".github/copilot-instructions.md"
    ];

    private readonly GitService _gitService;
    private readonly GitIgnoreService _gitIgnoreService;
    private readonly SwaggerParser _swaggerParser;

    public RepoScanner(GitService gitService, GitIgnoreService gitIgnoreService, SwaggerParser swaggerParser)
    {
        _gitService = gitService;
        _gitIgnoreService = gitIgnoreService;
        _swaggerParser = swaggerParser;
    }

    public async Task<DashboardMetadata> ScanAsync(AppOptions options, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var rootPath = options.TargetPath;
        var projectName = new DirectoryInfo(rootPath).Name;
        var branch = options.BranchOverride ?? await _gitService.GetCurrentBranchAsync(rootPath, cancellationToken);
        var generatedAt = DateTimeOffset.UtcNow;
        var lastCommitTime = await _gitService.GetLastCommitTimeAsync(rootPath, cancellationToken)
                             ?? new DateTimeOffset(Directory.GetLastWriteTimeUtc(rootPath), TimeSpan.Zero);

        var allFiles = EnumerateFiles(rootPath, options.MaxFiles, warnings);
        var allRelativePaths = allFiles.Select(file => file.RelativePath).ToArray();
        var ignoredSet = await _gitIgnoreService.ResolveIgnoredFilesAsync(rootPath, allRelativePaths, cancellationToken);

        var mandatorySet = MandatoryRelativePaths
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scannedFiles = allFiles
            .Where(file => mandatorySet.Contains(file.RelativePath) || !ignoredSet.Contains(file.RelativePath))
            .ToArray();

        var mandatoryFound = MandatoryRelativePaths
            .Where(path => File.Exists(Path.Combine(rootPath, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        var mandatoryMissing = MandatoryRelativePaths
            .Except(mandatoryFound, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (mandatoryMissing.Length > 0)
        {
            warnings.Add($"必要檔案缺漏: {string.Join(", ", mandatoryMissing)}");
        }

        var languages = DetectLanguages(scannedFiles);
        var dependencies = ExtractDependencies(rootPath, scannedFiles, warnings);
        var routes = ExtractRoutes(rootPath, scannedFiles);
        var sqlFiles = scannedFiles
            .Where(file => file.RelativePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var swaggerCandidates = scannedFiles
            .Where(file => IsSwaggerCandidate(file.RelativePath))
            .Select(file => file.RelativePath)
            .ToArray();

        var (apiSpecs, swaggerWarnings) = await _swaggerParser.ParseAsync(
            rootPath,
            swaggerCandidates,
            routes,
            options.UseCopilotTranslation,
            options.CopilotModel,
            cancellationToken);
        warnings.AddRange(swaggerWarnings);

        var ciWorkflows = ExtractCiWorkflows(rootPath, scannedFiles);
        var buildGuide = BuildGuide(rootPath, languages, scannedFiles);
        var fileTree = BuildTree(scannedFiles.Select(file => file.RelativePath), maxDepth: 5, maxChildren: 80);
        var envInfo = ReadEnvInfo(rootPath);
        var copilotInstructions = ReadCopilotInstructions(rootPath);
        var description = ExtractProjectDescription(rootPath);

        return new DashboardMetadata(
            Project: new ProjectInfo(
                Name: projectName,
                Path: rootPath,
                Branch: branch,
                LastUpdatedUtc: lastCommitTime.UtcDateTime.ToString("O"),
                GeneratedAtUtc: generatedAt.UtcDateTime.ToString("O"),
                Description: description),
            Settings: new ScanSettings(
                OutputRoot: options.OutputRoot,
                ResultDirName: options.ResultDirName,
                UseCopilotTranslation: options.UseCopilotTranslation,
                CopilotModel: options.CopilotModel),
            Stats: new ScanStats(
                TotalFiles: allFiles.Count,
                IgnoredFiles: ignoredSet.Count,
                ScannedFiles: scannedFiles.Length,
                TotalApiEndpoints: apiSpecs.Sum(spec => spec.Endpoints.Count),
                TotalRoutes: routes.Count),
            MandatoryFiles: new MandatoryFileStatus(mandatoryFound, mandatoryMissing),
            EnvironmentVariables: envInfo,
            CopilotInstructions: copilotInstructions,
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Languages: languages,
            Dependencies: dependencies,
            Routes: routes,
            SqlFiles: sqlFiles,
            ApiSpecs: apiSpecs,
            CiWorkflows: ciWorkflows,
            BuildGuide: buildGuide,
            FileTree: fileTree);
    }

    private static IReadOnlyList<RepoFile> EnumerateFiles(string rootPath, int maxFiles, ICollection<string> warnings)
    {
        var results = new List<RepoFile>();
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                var name = Path.GetFileName(subdir);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                stack.Push(subdir);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(rootPath, file)
                    .Replace('\\', '/');
                var info = new FileInfo(file);
                results.Add(new RepoFile(relative, file, info.Exists ? info.Length : 0));

                if (results.Count >= maxFiles)
                {
                    warnings.Add($"檔案數已達上限 {maxFiles}，後續檔案未納入掃描。");
                    return results;
                }
            }
        }

        return results;
    }

    private static IReadOnlyList<LanguageInfo> DetectLanguages(IReadOnlyList<RepoFile> files)
    {
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.RelativePath);
            string? language = extension.ToLowerInvariant() switch
            {
                ".go" => "Go",
                ".cs" => "C#",
                ".js" => "JavaScript",
                ".jsx" => "JavaScript",
                ".ts" => "TypeScript",
                ".tsx" => "TypeScript",
                ".py" => "Python",
                ".java" => "Java",
                ".php" => "PHP",
                ".rb" => "Ruby",
                ".sql" => "SQL",
                ".yaml" => "YAML",
                ".yml" => "YAML",
                ".json" => "JSON",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            counters.TryGetValue(language, out var count);
            counters[language] = count + 1;
        }

        return counters
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new LanguageInfo(pair.Key, pair.Value))
            .ToArray();
    }

    private static IReadOnlyList<DependencyInfo> ExtractDependencies(
        string rootPath,
        IReadOnlyList<RepoFile> files,
        ICollection<string> warnings)
    {
        var dependencies = new List<DependencyInfo>();
        dependencies.AddRange(ParseGoDependencies(rootPath, files));
        dependencies.AddRange(ParseNodeDependencies(rootPath, files));
        dependencies.AddRange(ParseDotnetDependencies(rootPath, files, warnings));
        dependencies.AddRange(ParsePythonDependencies(rootPath, files));
        dependencies.AddRange(ParseDockerDependencies(rootPath, files));

        return dependencies
            .DistinctBy(item => $"{item.Ecosystem}:{item.Name}:{item.Version}:{item.SourceFile}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Ecosystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<DependencyInfo> ParseGoDependencies(string rootPath, IReadOnlyList<RepoFile> files)
    {
        var goMod = files.FirstOrDefault(file => file.RelativePath.Equals("go.mod", StringComparison.OrdinalIgnoreCase));
        if (goMod is null)
        {
            return [];
        }

        var fullPath = Path.Combine(rootPath, goMod.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return [];
        }

        var dependencies = new List<DependencyInfo>();
        var inRequireBlock = false;
        foreach (var rawLine in File.ReadLines(fullPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("require (", StringComparison.Ordinal))
            {
                inRequireBlock = true;
                continue;
            }

            if (inRequireBlock && line == ")")
            {
                inRequireBlock = false;
                continue;
            }

            if (!inRequireBlock && !line.StartsWith("require ", StringComparison.Ordinal))
            {
                continue;
            }

            var content = inRequireBlock ? line : line["require ".Length..];
            var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                dependencies.Add(new DependencyInfo("go-module", parts[0], parts[1], goMod.RelativePath));
            }
        }

        return dependencies;
    }

    private static IEnumerable<DependencyInfo> ParseNodeDependencies(string rootPath, IReadOnlyList<RepoFile> files)
    {
        var packageJson = files.FirstOrDefault(file => file.RelativePath.Equals("package.json", StringComparison.OrdinalIgnoreCase));
        if (packageJson is null)
        {
            return [];
        }

        var fullPath = Path.Combine(rootPath, packageJson.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return [];
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fullPath));
            var deps = new List<DependencyInfo>();
            ReadDependencyGroup(doc.RootElement, "dependencies", "npm", packageJson.RelativePath, deps);
            ReadDependencyGroup(doc.RootElement, "devDependencies", "npm-dev", packageJson.RelativePath, deps);
            return deps;
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<DependencyInfo> ParseDotnetDependencies(
        string rootPath,
        IReadOnlyList<RepoFile> files,
        ICollection<string> warnings)
    {
        var csprojFiles = files
            .Where(file => file.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var dependencies = new List<DependencyInfo>();
        foreach (var csproj in csprojFiles)
        {
            var fullPath = Path.Combine(rootPath, csproj.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                var xml = XDocument.Load(fullPath);
                foreach (var packageRef in xml.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
                {
                    var name = packageRef.Attribute("Include")?.Value ?? string.Empty;
                    var version = packageRef.Attribute("Version")?.Value
                                  ?? packageRef.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value
                                  ?? "unknown";
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    dependencies.Add(new DependencyInfo("nuget", name, version, csproj.RelativePath));
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"解析 {csproj.RelativePath} 套件失敗: {ex.Message}");
            }
        }

        return dependencies;
    }

    private static IEnumerable<DependencyInfo> ParsePythonDependencies(string rootPath, IReadOnlyList<RepoFile> files)
    {
        var requirements = files
            .Where(file => Path.GetFileName(file.RelativePath).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var result = new List<DependencyInfo>();
        foreach (var requirement in requirements)
        {
            var fullPath = Path.Combine(rootPath, requirement.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(fullPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var parts = line.Split("==", StringSplitOptions.TrimEntries);
                var name = parts[0];
                var version = parts.Length > 1 ? parts[1] : "latest";
                result.Add(new DependencyInfo("python", name, version, requirement.RelativePath));
            }
        }

        return result;
    }

    private static IEnumerable<DependencyInfo> ParseDockerDependencies(string rootPath, IReadOnlyList<RepoFile> files)
    {
        var dockerFiles = files
            .Where(file => Path.GetFileName(file.RelativePath).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var result = new List<DependencyInfo>();

        foreach (var dockerFile in dockerFiles)
        {
            var fullPath = Path.Combine(rootPath, dockerFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(fullPath))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var image = line["FROM ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(image))
                {
                    result.Add(new DependencyInfo("docker", image, "image", dockerFile.RelativePath));
                }
            }
        }

        return result;
    }

    private static void ReadDependencyGroup(
        System.Text.Json.JsonElement root,
        string propertyName,
        string ecosystem,
        string sourceFile,
        ICollection<DependencyInfo> collector)
    {
        if (!root.TryGetProperty(propertyName, out var deps) || deps.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        foreach (var item in deps.EnumerateObject())
        {
            collector.Add(new DependencyInfo(ecosystem, item.Name, item.Value.GetString() ?? "unknown", sourceFile));
        }
    }

    private static IReadOnlyList<RouteInfo> ExtractRoutes(string rootPath, IReadOnlyList<RepoFile> files)
    {
        var routes = new List<RouteInfo>();
        var routeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Where(CanParseRoute))
        {
            var fullPath = Path.Combine(rootPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch
            {
                continue;
            }

            foreach (Match match in RouterMethodRegex().Matches(content))
            {
                var method = match.Groups["method"].Value.ToUpperInvariant();
                var path = match.Groups["path"].Value;
                AddRoute(routes, routeKeys, method, path, file.RelativePath);
            }

            foreach (Match match in AspNetMapRegex().Matches(content))
            {
                var method = match.Groups["method"].Value.Replace("Map", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
                var path = match.Groups["path"].Value;
                AddRoute(routes, routeKeys, method, path, file.RelativePath);
            }

            foreach (Match match in HandleFuncRegex().Matches(content))
            {
                var path = match.Groups["path"].Value;
                AddRoute(routes, routeKeys, "ANY", path, file.RelativePath);
            }

            foreach (Match match in HttpAnnotationRegex().Matches(content))
            {
                var method = match.Groups["method"].Value.ToUpperInvariant();
                var path = match.Groups["path"].Value;
                AddRoute(routes, routeKeys, method, path, file.RelativePath);
            }
        }

        return routes
            .OrderBy(route => route.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(route => route.Method, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool CanParseRoute(RepoFile file)
    {
        if (file.SizeBytes is <= 0 or > 512_000)
        {
            return false;
        }

        var extension = Path.GetExtension(file.RelativePath);
        if (!RouteFileExtensions.Contains(extension))
        {
            return false;
        }

        return !file.RelativePath.Contains("/vendor/", StringComparison.OrdinalIgnoreCase)
               && !file.RelativePath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddRoute(ICollection<RouteInfo> routes, ISet<string> keys, string method, string path, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            return;
        }

        var key = $"{method}:{path}:{sourceFile}";
        if (!keys.Add(key))
        {
            return;
        }

        routes.Add(new RouteInfo(method, path, sourceFile));
    }

    private static IReadOnlyList<CiWorkflowInfo> ExtractCiWorkflows(string rootPath, IReadOnlyList<RepoFile> files)
    {
        var workflows = new List<CiWorkflowInfo>();
        foreach (var file in files.Where(file =>
                     file.RelativePath.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) &&
                     (file.RelativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                      file.RelativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))))
        {
            var fullPath = Path.Combine(rootPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var lines = File.ReadAllLines(fullPath);
            var name = Path.GetFileNameWithoutExtension(file.RelativePath);
            var trigger = "unknown";

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase) && name == Path.GetFileNameWithoutExtension(file.RelativePath))
                {
                    name = trimmed["name:".Length..].Trim();
                }

                if (trimmed.StartsWith("on:", StringComparison.OrdinalIgnoreCase))
                {
                    var inline = trimmed["on:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(inline))
                    {
                        trigger = inline;
                    }
                    else
                    {
                        var events = new List<string>();
                        for (var j = i + 1; j < lines.Length; j++)
                        {
                            if (!lines[j].StartsWith("  ", StringComparison.Ordinal))
                            {
                                break;
                            }

                            var evt = lines[j].Trim().TrimEnd(':');
                            if (!string.IsNullOrWhiteSpace(evt))
                            {
                                events.Add(evt);
                            }
                        }

                        if (events.Count > 0)
                        {
                            trigger = string.Join(", ", events);
                        }
                    }

                    break;
                }
            }

            workflows.Add(new CiWorkflowInfo(name, file.RelativePath, trigger));
        }

        return workflows
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static BuildRunGuide BuildGuide(string rootPath, IReadOnlyList<LanguageInfo> languages, IReadOnlyList<RepoFile> files)
    {
        var build = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var run = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var test = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://docs.github.com/actions",
            "https://docs.github.com/copilot"
        };

        var languageSet = languages.Select(language => language.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (languageSet.Contains("Go"))
        {
            build.Add("go build ./...");
            run.Add("go run .");
            test.Add("go test ./...");
            links.Add("https://go.dev/doc/");
        }

        if (languageSet.Contains("C#"))
        {
            build.Add("dotnet build");
            run.Add("dotnet run");
            test.Add("dotnet test");
            links.Add("https://learn.microsoft.com/dotnet/");
        }

        if (languageSet.Contains("TypeScript") || languageSet.Contains("JavaScript"))
        {
            build.Add("npm run build");
            run.Add("npm run start");
            test.Add("npm test");
            links.Add("https://docs.npmjs.com/");
        }

        var makefile = files.FirstOrDefault(file => file.RelativePath.Equals("Makefile", StringComparison.OrdinalIgnoreCase));
        if (makefile is not null)
        {
            var fullPath = Path.Combine(rootPath, makefile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                foreach (var line in File.ReadLines(fullPath))
                {
                    var match = MakeTargetRegex().Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var target = match.Groups["target"].Value;
                    if (target.Contains("build", StringComparison.OrdinalIgnoreCase))
                    {
                        build.Add($"make {target}");
                    }
                    else if (target.Contains("run", StringComparison.OrdinalIgnoreCase) ||
                             target.Contains("start", StringComparison.OrdinalIgnoreCase))
                    {
                        run.Add($"make {target}");
                    }
                    else if (target.Contains("test", StringComparison.OrdinalIgnoreCase))
                    {
                        test.Add($"make {target}");
                    }
                }
            }
        }

        return new BuildRunGuide(
            BuildCommands: build.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            RunCommands: run.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            TestCommands: test.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            ResourceLinks: links.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static TreeNode BuildTree(IEnumerable<string> relativePaths, int maxDepth, int maxChildren)
    {
        var root = new MutableTreeNode(".", "directory");
        foreach (var relativePath in relativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var cursor = root;

            for (var index = 0; index < parts.Length; index++)
            {
                if (index >= maxDepth)
                {
                    cursor = cursor.GetOrAddChild("...", "truncated");
                    break;
                }

                var isLeaf = index == parts.Length - 1;
                var nodeType = isLeaf ? "file" : "directory";
                cursor = cursor.GetOrAddChild(parts[index], nodeType);
            }
        }

        return ToTreeNode(root, maxChildren);
    }

    private static TreeNode ToTreeNode(MutableTreeNode node, int maxChildren)
    {
        var orderedChildren = node.Children
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxChildren)
            .Select(child => ToTreeNode(child, maxChildren))
            .ToList();

        if (node.Children.Count > maxChildren)
        {
            orderedChildren.Add(new TreeNode($"... 還有 {node.Children.Count - maxChildren} 項", "truncated", []));
        }

        return new TreeNode(node.Name, node.NodeType, orderedChildren);
    }

    private static EnvInfo ReadEnvInfo(string rootPath)
    {
        var envPath = Path.Combine(rootPath, ".env");
        if (!File.Exists(envPath))
        {
            return new EnvInfo(false, []);
        }

        var keys = new List<string>();
        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            var key = line.Split('=', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key);
            }
        }

        return new EnvInfo(true, keys.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static CopilotInstructionsInfo ReadCopilotInstructions(string rootPath)
    {
        const string relativePath = ".github/copilot-instructions.md";
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return new CopilotInstructionsInfo(false, relativePath, string.Empty);
        }

        var summary = string.Join(
            " ",
            File.ReadLines(fullPath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(6));

        return new CopilotInstructionsInfo(true, relativePath, summary.Length > 280 ? $"{summary[..280]}..." : summary);
    }

    private static string ExtractProjectDescription(string rootPath)
    {
        var candidates = new[]
        {
            "README.md",
            "readme.md",
            "README.MD"
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.Combine(rootPath, candidate);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(fullPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('['))
                {
                    continue;
                }

                return line.Length > 220 ? $"{line[..220]}..." : line;
            }
        }

        return "未在 README 找到專案摘要。";
    }

    private static bool IsSwaggerCandidate(string relativePath)
    {
        var lower = relativePath.ToLowerInvariant();
        if (!lower.EndsWith(".json"))
        {
            return false;
        }

        return lower.Contains("swagger") || lower.Contains("openapi");
    }

    [GeneratedRegex(@"(?<target>[a-zA-Z0-9_.-]+)\s*:", RegexOptions.Compiled)]
    private static partial Regex MakeTargetRegex();

    [GeneratedRegex(@"(?:router|r|app)\.(?<method>GET|POST|PUT|DELETE|PATCH|OPTIONS|HEAD)\s*\(\s*[""`](?<path>/[^""`]+)[""`]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RouterMethodRegex();

    [GeneratedRegex(@"(?<method>MapGet|MapPost|MapPut|MapDelete|MapPatch)\s*\(\s*""(?<path>/[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AspNetMapRegex();

    [GeneratedRegex(@"HandleFunc\(\s*""(?<path>/[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HandleFuncRegex();

    [GeneratedRegex(@"Http(?<method>Get|Post|Put|Delete|Patch)\(\s*""(?<path>/[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HttpAnnotationRegex();

    private sealed class MutableTreeNode
    {
        public MutableTreeNode(string name, string nodeType)
        {
            Name = name;
            NodeType = nodeType;
        }

        public string Name { get; }

        public string NodeType { get; }

        public List<MutableTreeNode> Children { get; } = [];

        public MutableTreeNode GetOrAddChild(string name, string nodeType)
        {
            var existing = Children.FirstOrDefault(child =>
                child.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            var created = new MutableTreeNode(name, nodeType);
            Children.Add(created);
            return created;
        }
    }
}
