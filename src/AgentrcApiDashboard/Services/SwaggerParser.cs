using System.Text.Json;
using AgentrcApiDashboard.Models;

namespace AgentrcApiDashboard.Services;

public sealed class SwaggerParser
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get",
        "post",
        "put",
        "delete",
        "patch",
        "options",
        "head"
    };

    private readonly CopilotTranslationService _translator;

    public SwaggerParser(CopilotTranslationService translator)
    {
        _translator = translator;
    }

    public async Task<(IReadOnlyList<ApiSpecInfo> Specs, IReadOnlyList<string> Warnings)> ParseAsync(
        string rootPath,
        IReadOnlyList<string> swaggerRelativePaths,
        IReadOnlyList<RouteInfo> routes,
        bool useCopilotTranslation,
        string model,
        CancellationToken cancellationToken)
    {
        var specs = new List<ApiSpecInfo>();
        var warnings = new List<string>();

        foreach (var relativePath in swaggerRelativePaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fullPath, cancellationToken));
                if (!document.RootElement.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var endpoints = new List<ApiEndpointInfo>();
                var translationRequests = new List<ApiTranslationRequest>();

                foreach (var pathEntry in paths.EnumerateObject())
                {
                    var apiPath = pathEntry.Name;
                    if (pathEntry.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var methodEntry in pathEntry.Value.EnumerateObject())
                    {
                        if (!HttpMethods.Contains(methodEntry.Name))
                        {
                            continue;
                        }

                        var method = methodEntry.Name.ToUpperInvariant();
                        var operation = methodEntry.Value;
                        var summary = ReadString(operation, "summary");
                        var description = ReadString(operation, "description");
                        var key = $"{method} {apiPath}";
                        var sourceHint = ResolveSourceHint(routes, method, apiPath);
                        var fallbackUsage = BuildFallbackUsage(method, apiPath, summary, description);
                        var fallbackFlow = BuildFallbackFlow(method, apiPath, sourceHint);

                        var endpoint = new ApiEndpointInfo(
                            Method: method,
                            Path: apiPath,
                            Summary: summary,
                            Description: description,
                            TraditionalChineseUsage: fallbackUsage,
                            SourceHint: sourceHint,
                            Parameters: ParseParameters(operation),
                            Responses: ParseResponses(operation),
                            FlowSteps: fallbackFlow);

                        endpoints.Add(endpoint);
                        translationRequests.Add(new ApiTranslationRequest(key, method, apiPath, summary, description));
                    }
                }

                if (useCopilotTranslation && translationRequests.Count > 0)
                {
                    var limitedRequests = translationRequests.Take(120).ToArray();
                    var (translations, warning) = await _translator.TranslateAsync(limitedRequests, model, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(warning))
                    {
                        warnings.Add(warning);
                    }

                    if (translationRequests.Count > limitedRequests.Length)
                    {
                        warnings.Add($"Swagger {relativePath} 共有 {translationRequests.Count} 支 API，Copilot 翻譯僅處理前 {limitedRequests.Length} 支，剩餘使用規則式描述。");
                    }

                    endpoints = endpoints
                        .Select(endpoint =>
                        {
                            var key = $"{endpoint.Method} {endpoint.Path}";
                            if (!translations.TryGetValue(key, out var translated))
                            {
                                return endpoint;
                            }

                            var usage = string.IsNullOrWhiteSpace(translated.UsageZh) ? endpoint.TraditionalChineseUsage : translated.UsageZh;
                            var flow = translated.FlowStepsZh.Count == 0 ? endpoint.FlowSteps : translated.FlowStepsZh;
                            return endpoint with
                            {
                                TraditionalChineseUsage = usage,
                                FlowSteps = flow
                            };
                        })
                        .ToList();
                }

                specs.Add(new ApiSpecInfo(relativePath, endpoints));
            }
            catch (Exception ex)
            {
                warnings.Add($"解析 Swagger 失敗: {relativePath} ({ex.Message})");
            }
        }

        return (specs, warnings);
    }

    private static string ResolveSourceHint(IReadOnlyList<RouteInfo> routes, string method, string path)
    {
        var matched = routes.FirstOrDefault(route =>
            route.Method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
            route.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (matched is not null)
        {
            return matched.SourceFile;
        }

        matched = routes.FirstOrDefault(route =>
            route.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        return matched?.SourceFile ?? "未比對到對應程式路徑";
    }

    private static IReadOnlyList<ApiParameterInfo> ParseParameters(JsonElement operation)
    {
        var items = new List<ApiParameterInfo>();
        if (!operation.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var parameter in parameters.EnumerateArray())
        {
            var name = ReadString(parameter, "name");
            var location = ReadString(parameter, "in");
            var required = parameter.TryGetProperty("required", out var requiredElement) &&
                           requiredElement.ValueKind == JsonValueKind.True;
            var description = ReadString(parameter, "description");

            var type = string.Empty;
            if (parameter.TryGetProperty("schema", out var schema))
            {
                type = ReadString(schema, "type");
                if (string.IsNullOrWhiteSpace(type))
                {
                    type = ReadString(schema, "$ref");
                }
            }

            items.Add(new ApiParameterInfo(name, location, required, type, description));
        }

        return items;
    }

    private static IReadOnlyList<ApiResponseInfo> ParseResponses(JsonElement operation)
    {
        var items = new List<ApiResponseInfo>();
        if (!operation.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object)
        {
            return items;
        }

        foreach (var response in responses.EnumerateObject())
        {
            var description = ReadString(response.Value, "description");
            var schemaType = string.Empty;

            if (response.Value.TryGetProperty("schema", out var schema))
            {
                schemaType = ReadString(schema, "type");
                if (string.IsNullOrWhiteSpace(schemaType))
                {
                    schemaType = ReadString(schema, "$ref");
                }
            }

            if (string.IsNullOrWhiteSpace(schemaType) &&
                response.Value.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Object)
            {
                var firstContent = content.EnumerateObject().FirstOrDefault();
                if (firstContent.Value.ValueKind == JsonValueKind.Object &&
                    firstContent.Value.TryGetProperty("schema", out var contentSchema))
                {
                    schemaType = ReadString(contentSchema, "type");
                    if (string.IsNullOrWhiteSpace(schemaType))
                    {
                        schemaType = ReadString(contentSchema, "$ref");
                    }
                }
            }

            items.Add(new ApiResponseInfo(response.Name, description, schemaType));
        }

        return items;
    }

    private static IReadOnlyList<string> BuildFallbackFlow(string method, string path, string sourceHint)
    {
        return
        [
            $"客戶端發送 {method} {path} 請求",
            $"由路由層 ({sourceHint}) 接收並驗證參數",
            "進入 service/domain 邏輯處理",
            "整理回傳格式並送回呼叫端"
        ];
    }

    private static string BuildFallbackUsage(string method, string path, string summary, string description)
    {
        var summaryText = string.IsNullOrWhiteSpace(summary) ? "此 API" : summary;
        var detail = string.IsNullOrWhiteSpace(description) ? "請確認參數與回傳欄位。" : description;
        return $"{summaryText}。透過 {method} {path} 呼叫，送出符合 Swagger 定義的參數即可取得對應結果。{detail}";
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
