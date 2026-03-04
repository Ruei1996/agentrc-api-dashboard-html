using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentrcApiDashboard.Cli;
using AgentrcApiDashboard.Models;

namespace AgentrcApiDashboard.Services;

public sealed class OutputService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public OutputArtifactPaths WriteOutputs(DashboardMetadata metadata, string html, AppOptions options)
    {
        var outputDirectory = Path.Combine(options.OutputRoot, options.ResultDirName);
        Directory.CreateDirectory(outputDirectory);

        var repoName = Slugify(metadata.Project.Name);
        var branchName = Slugify(metadata.Project.Branch);

        var jsonPath = Path.Combine(outputDirectory, $"{repoName}-dashboard-meta-data-{branchName}.json");
        var htmlPath = Path.Combine(outputDirectory, $"{repoName}-dashboard-{branchName}.html");

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(htmlPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new OutputArtifactPaths(jsonPath, htmlPath);
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var slug = Regex.Replace(value, @"[^A-Za-z0-9._-]+", "-", RegexOptions.Compiled);
        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
    }
}
