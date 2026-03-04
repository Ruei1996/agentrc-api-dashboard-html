using System.Text;
using System.Text.Json;
using AgentrcApiDashboard.Models;
using GitHub.Copilot.SDK;

namespace AgentrcApiDashboard.Services;

public sealed class CopilotTranslationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<(Dictionary<string, ApiTranslationResult> Translations, string? Warning)> TranslateAsync(
        IReadOnlyList<ApiTranslationRequest> requests,
        string model,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return (new Dictionary<string, ApiTranslationResult>(StringComparer.OrdinalIgnoreCase), null);
        }

        try
        {
            await using var client = new CopilotClient(new CopilotClientOptions
            {
                LogLevel = "error",
                Cwd = Environment.CurrentDirectory
            });
            await client.StartAsync();

            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Model = model,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Content =
                        """
                        你是 API 文件工程師。你只會輸出 JSON，不要輸出 markdown、說明文字、程式碼區塊。
                        目標是把 Swagger endpoint 內容轉成繁體中文使用說明，並產生簡短流程步驟。
                        """
                }
            });

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var assistantOutput = new StringBuilder();

            session.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageEvent msg:
                        assistantOutput.AppendLine(msg.Data.Content);
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                }
            });

            var payload = JsonSerializer.Serialize(requests, JsonOptions);
            var prompt =
                $$"""
                  針對以下 JSON endpoint 清單產生繁體中文說明，格式必須是:
                  {"items":[{"key":"GET /path","usageZh":"...","flowStepsZh":["步驟1","步驟2"]}]}

                  規則:
                  - key 必須完全對應輸入 key
                  - usageZh 50~120 字，描述 API 用法
                  - flowStepsZh 3~5 個步驟
                  - 只輸出 JSON

                  輸入:
                  {{payload}}
                  """;

            await session.SendAsync(new MessageOptions { Prompt = prompt });
            await done.Task.WaitAsync(TimeSpan.FromSeconds(120), cancellationToken);
            await client.StopAsync();

            var translations = ParseTranslations(assistantOutput.ToString());
            return (translations, null);
        }
        catch (Exception ex)
        {
            return (new Dictionary<string, ApiTranslationResult>(StringComparer.OrdinalIgnoreCase), $"Copilot SDK 翻譯失敗，改用規則式描述: {ex.Message}");
        }
    }

    private static Dictionary<string, ApiTranslationResult> ParseTranslations(string raw)
    {
        var result = new Dictionary<string, ApiTranslationResult>(StringComparer.OrdinalIgnoreCase);
        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!TryGetString(item, "key", out var key))
            {
                continue;
            }

            var usageZh = TryGetString(item, "usageZh", out var usage) ? usage : string.Empty;
            var steps = new List<string>();
            if (item.TryGetProperty("flowStepsZh", out var flowSteps) && flowSteps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in flowSteps.EnumerateArray())
                {
                    if (step.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(step.GetString()))
                    {
                        steps.Add(step.GetString()!);
                    }
                }
            }

            if (steps.Count == 0)
            {
                steps.Add("接收請求");
                steps.Add("執行業務邏輯");
                steps.Add("回傳結果");
            }

            result[key] = new ApiTranslationResult(usageZh, steps);
        }

        return result;
    }

    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var cleaned = raw.Trim();
        cleaned = cleaned.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase);
        cleaned = cleaned.Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase);

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return cleaned[start..(end + 1)];
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var item) && item.ValueKind == JsonValueKind.String)
        {
            value = item.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
