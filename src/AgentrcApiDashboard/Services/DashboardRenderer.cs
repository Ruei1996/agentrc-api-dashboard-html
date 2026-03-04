using System.Net;
using System.Text;
using System.Text.Json;
using AgentrcApiDashboard.Models;

namespace AgentrcApiDashboard.Services;

public sealed class DashboardRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Render(DashboardMetadata metadata)
    {
        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
        var apiRows = RenderApiRows(metadata);
        var routeRows = RenderRouteRows(metadata.Routes);
        var dependencyRows = RenderDependencyRows(metadata.Dependencies);
        var sqlRows = RenderSqlRows(metadata.SqlFiles);
        var workflowRows = RenderWorkflowRows(metadata.CiWorkflows);
        var languageTags = string.Join(
            string.Empty,
            metadata.Languages.Select(language =>
                $"<span class=\"tag\">{Encode(language.Name)} ({language.FileCount})</span>"));

        var warnings = metadata.Warnings.Count == 0
            ? "<li>無</li>"
            : string.Join(string.Empty, metadata.Warnings.Select(warning => $"<li>{Encode(warning)}</li>"));

        var mandatoryFound = metadata.MandatoryFiles.Found.Count == 0
            ? "<li>無</li>"
            : string.Join(string.Empty, metadata.MandatoryFiles.Found.Select(path => $"<li>{Encode(path)}</li>"));
        var mandatoryMissing = metadata.MandatoryFiles.Missing.Count == 0
            ? "<li>無</li>"
            : string.Join(string.Empty, metadata.MandatoryFiles.Missing.Select(path => $"<li>{Encode(path)}</li>"));

        return $$"""
                 <!doctype html>
                 <html lang="zh-Hant">
                 <head>
                   <meta charset="utf-8">
                   <meta name="viewport" content="width=device-width, initial-scale=1">
                   <title>{{Encode(metadata.Project.Name)}} Dashboard</title>
                   <style>
                     :root {
                       --bg: #0b1020;
                       --panel: #151b2f;
                       --card: #1c2440;
                       --text: #e6e9f2;
                       --muted: #9aa4bf;
                       --accent: #6ea8fe;
                       --ok: #3ddc97;
                       --warn: #f5c16c;
                     }
                     * { box-sizing: border-box; }
                     body {
                       margin: 0;
                       font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                       color: var(--text);
                       background: var(--bg);
                     }
                     header, footer {
                       background: var(--panel);
                       padding: 16px 24px;
                       border-bottom: 1px solid #2a3356;
                     }
                     footer {
                       border-top: 1px solid #2a3356;
                       border-bottom: none;
                       color: var(--muted);
                     }
                     .layout {
                       display: grid;
                       grid-template-columns: 260px 1fr;
                       min-height: calc(100vh - 126px);
                     }
                     aside {
                       border-right: 1px solid #2a3356;
                       background: var(--panel);
                       padding: 18px;
                     }
                     aside nav a {
                       display: block;
                       color: var(--text);
                       text-decoration: none;
                       margin: 8px 0;
                       padding: 8px 10px;
                       border-radius: 8px;
                     }
                     aside nav a:hover { background: #233058; }
                     main {
                       padding: 20px;
                       overflow-x: auto;
                     }
                     section {
                       background: var(--card);
                       border: 1px solid #2b3762;
                       border-radius: 12px;
                       padding: 16px;
                       margin-bottom: 16px;
                     }
                     .cards {
                       display: grid;
                       grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
                       gap: 12px;
                     }
                     .card {
                       background: #222d50;
                       border-radius: 10px;
                       padding: 12px;
                     }
                     .label { color: var(--muted); font-size: 12px; }
                     .value { margin-top: 6px; font-size: 14px; word-break: break-word; }
                     .tag {
                       display: inline-block;
                       padding: 4px 10px;
                       border-radius: 999px;
                       margin: 0 8px 8px 0;
                       background: #2a3a68;
                       font-size: 12px;
                     }
                     input[type="search"] {
                       width: 100%;
                       background: #0f1833;
                       color: var(--text);
                       border: 1px solid #344273;
                       border-radius: 8px;
                       padding: 10px;
                       margin-bottom: 10px;
                     }
                     table {
                       width: 100%;
                       border-collapse: collapse;
                       font-size: 13px;
                     }
                     th, td {
                       text-align: left;
                       border-bottom: 1px solid #314173;
                       padding: 8px;
                       vertical-align: top;
                     }
                     th { color: var(--muted); }
                     button {
                       background: #2f4c8e;
                       color: white;
                       border: none;
                       border-radius: 6px;
                       padding: 6px 10px;
                       cursor: pointer;
                     }
                     button:hover { background: #3a61b5; }
                     .tree ul {
                       list-style: none;
                       margin: 0;
                       padding-left: 16px;
                     }
                     .tree li { margin: 4px 0; }
                     .node.directory::before { content: "📁 "; }
                     .node.file::before { content: "📄 "; }
                     .node.truncated::before { content: "… "; color: var(--warn); }
                     .status-ok { color: var(--ok); }
                     .status-warn { color: var(--warn); }
                     @media (max-width: 960px) {
                       .layout { grid-template-columns: 1fr; }
                       aside { border-right: none; border-bottom: 1px solid #2a3356; }
                     }
                   </style>
                 </head>
                 <body>
                   <header>
                     <h1 style="margin:0 0 8px 0;">{{Encode(metadata.Project.Name)}} Dashboard</h1>
                     <div style="color:#9aa4bf;font-size:13px;">
                       Branch: <strong>{{Encode(metadata.Project.Branch)}}</strong> |
                       Generated UTC: {{Encode(metadata.Project.GeneratedAtUtc)}} |
                       Last Commit UTC: {{Encode(metadata.Project.LastUpdatedUtc)}}
                     </div>
                   </header>

                   <div class="layout">
                     <aside>
                       <nav>
                         <a href="#overview">總覽</a>
                         <a href="#mandatory">必要檔案檢查</a>
                         <a href="#deps">依賴與語言</a>
                         <a href="#tree">專案樹狀圖</a>
                         <a href="#api">API 詳細資訊</a>
                         <a href="#routes">路由</a>
                         <a href="#sql">SQL 路徑</a>
                         <a href="#ci">CI/CD</a>
                         <a href="#guide">建置與執行指南</a>
                         <a href="#warnings">警告</a>
                       </nav>
                     </aside>

                     <main>
                       <section id="overview">
                         <h2>總覽</h2>
                         <p>{{Encode(metadata.Project.Description)}}</p>
                         <div class="cards">
                           <div class="card">
                             <div class="label">掃描目標</div>
                             <div class="value">{{Encode(metadata.Project.Path)}}</div>
                           </div>
                           <div class="card">
                             <div class="label">檔案統計</div>
                             <div class="value">總檔案 {{metadata.Stats.TotalFiles}} / 掃描 {{metadata.Stats.ScannedFiles}} / 忽略 {{metadata.Stats.IgnoredFiles}}</div>
                           </div>
                           <div class="card">
                             <div class="label">API / 路由</div>
                             <div class="value">API {{metadata.Stats.TotalApiEndpoints}} 支，路由 {{metadata.Stats.TotalRoutes}} 筆</div>
                           </div>
                           <div class="card">
                             <div class="label">Copilot SDK 翻譯</div>
                             <div class="value">{{(metadata.Settings.UseCopilotTranslation ? "已啟用" : "未啟用")}} ({{Encode(metadata.Settings.CopilotModel)}})</div>
                           </div>
                         </div>
                       </section>

                       <section id="mandatory">
                         <h2>必要檔案檢查</h2>
                         <div class="cards">
                           <div class="card">
                             <div class="label">已找到</div>
                             <ul class="status-ok">{{mandatoryFound}}</ul>
                           </div>
                           <div class="card">
                             <div class="label">缺漏</div>
                             <ul class="status-warn">{{mandatoryMissing}}</ul>
                           </div>
                           <div class="card">
                             <div class="label">.env</div>
                             <div class="value">{{(metadata.EnvironmentVariables.Exists ? "存在" : "不存在")}}</div>
                             <div class="label" style="margin-top:8px;">可用鍵值 (僅鍵名)</div>
                             <div class="value">{{Encode(string.Join(", ", metadata.EnvironmentVariables.Keys))}}</div>
                           </div>
                           <div class="card">
                             <div class="label">copilot-instructions 摘要</div>
                             <div class="value">{{Encode(metadata.CopilotInstructions.Summary)}}</div>
                           </div>
                         </div>
                       </section>

                       <section id="deps">
                         <h2>依賴與語言</h2>
                         <div style="margin-bottom:10px;">{{languageTags}}</div>
                         <input id="globalSearch" type="search" placeholder="搜尋 API、路由、SQL、依賴、CI...">
                         <table>
                           <thead>
                             <tr><th>Ecosystem</th><th>Name</th><th>Version</th><th>Source</th></tr>
                           </thead>
                           <tbody>
                             {{dependencyRows}}
                           </tbody>
                         </table>
                       </section>

                       <section id="tree">
                         <h2>專案樹狀圖 (節錄)</h2>
                         <div class="tree">{{RenderTree(metadata.FileTree)}}</div>
                       </section>

                       <section id="api">
                         <h2>API 詳細資訊 (Swagger / OpenAPI)</h2>
                         <table>
                           <thead>
                             <tr><th>Method</th><th>Path</th><th>Summary</th><th>繁中用法</th><th>來源路徑</th><th>詳細</th></tr>
                           </thead>
                           <tbody>
                             {{apiRows}}
                           </tbody>
                         </table>
                       </section>

                       <section id="routes">
                         <h2>路由清單</h2>
                         <table>
                           <thead>
                             <tr><th>Method</th><th>Path</th><th>Source</th></tr>
                           </thead>
                           <tbody>
                             {{routeRows}}
                           </tbody>
                         </table>
                       </section>

                       <section id="sql">
                         <h2>SQL 路徑</h2>
                         <table>
                           <thead>
                             <tr><th>SQL File</th></tr>
                           </thead>
                           <tbody>
                             {{sqlRows}}
                           </tbody>
                         </table>
                       </section>

                       <section id="ci">
                         <h2>CI/CD Workflow</h2>
                         <table>
                           <thead>
                             <tr><th>Name</th><th>Trigger</th><th>File</th></tr>
                           </thead>
                           <tbody>
                             {{workflowRows}}
                           </tbody>
                         </table>
                       </section>

                       <section id="guide">
                         <h2>建置與執行指南</h2>
                         <h3>Build</h3>
                         <ul>{{RenderSimpleList(metadata.BuildGuide.BuildCommands)}}</ul>
                         <h3>Run</h3>
                         <ul>{{RenderSimpleList(metadata.BuildGuide.RunCommands)}}</ul>
                         <h3>Test</h3>
                         <ul>{{RenderSimpleList(metadata.BuildGuide.TestCommands)}}</ul>
                         <h3>資源連結</h3>
                         <ul>{{RenderLinks(metadata.BuildGuide.ResourceLinks)}}</ul>
                       </section>

                       <section id="warnings">
                         <h2>警告訊息</h2>
                         <ul>{{warnings}}</ul>
                       </section>
                     </main>
                   </div>

                   <footer>
                     <div>Offline dashboard. 重新掃描後才會更新資料。</div>
                   </footer>

                   <script id="metadata-json" type="application/json">{{metadataJson}}</script>
                   <script>
                     const metadata = JSON.parse(document.getElementById('metadata-json').textContent);
                     const globalSearch = document.getElementById('globalSearch');
                     if (globalSearch) {
                       globalSearch.addEventListener('input', () => {
                         const query = globalSearch.value.trim().toLowerCase();
                         document.querySelectorAll('[data-search]').forEach((element) => {
                           const text = (element.dataset.search || '').toLowerCase();
                           element.style.display = query === '' || text.includes(query) ? '' : 'none';
                         });
                       });
                     }

                     function escapeHtml(raw) {
                       return String(raw ?? '')
                         .replaceAll('&', '&amp;')
                         .replaceAll('<', '&lt;')
                         .replaceAll('>', '&gt;')
                         .replaceAll('"', '&quot;');
                     }

                     window.openApiDetail = function(specIndex, endpointIndex) {
                       const spec = metadata.apiSpecs[specIndex];
                       const endpoint = spec.endpoints[endpointIndex];
                       const rows = (endpoint.parameters || [])
                         .map(p => `<tr><td>${escapeHtml(p.name)}</td><td>${escapeHtml(p.in)}</td><td>${p.required ? 'Y' : 'N'}</td><td>${escapeHtml(p.type)}</td><td>${escapeHtml(p.description)}</td></tr>`)
                         .join('');
                       const responseRows = (endpoint.responses || [])
                         .map(r => `<tr><td>${escapeHtml(r.statusCode)}</td><td>${escapeHtml(r.description)}</td><td>${escapeHtml(r.schemaType)}</td></tr>`)
                         .join('');
                       const flowRows = (endpoint.flowSteps || []).map(step => `<li>${escapeHtml(step)}</li>`).join('');

                       const html = `
                         <!doctype html>
                         <html lang="zh-Hant">
                         <head>
                           <meta charset="utf-8">
                           <title>${escapeHtml(endpoint.method)} ${escapeHtml(endpoint.path)}</title>
                           <style>
                             body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 20px; color: #1d2435; }
                             code { background: #eff3ff; padding: 2px 6px; border-radius: 6px; }
                             table { width: 100%; border-collapse: collapse; margin: 10px 0; }
                             th, td { border: 1px solid #d0d9ee; padding: 8px; text-align: left; }
                             h1, h2 { margin-bottom: 8px; }
                             .meta { color: #5d677f; margin-bottom: 12px; }
                           </style>
                         </head>
                         <body>
                           <h1>${escapeHtml(endpoint.method)} <code>${escapeHtml(endpoint.path)}</code></h1>
                           <div class="meta">Spec: ${escapeHtml(spec.relativePath)} | Source: ${escapeHtml(endpoint.sourceHint)}</div>
                           <h2>繁體中文用法</h2>
                           <p>${escapeHtml(endpoint.traditionalChineseUsage)}</p>
                           <h2>詳細流程圖 (文字版)</h2>
                           <ol>${flowRows}</ol>
                           <h2>參數說明</h2>
                           <table>
                             <thead><tr><th>Name</th><th>In</th><th>Required</th><th>Type</th><th>Description</th></tr></thead>
                             <tbody>${rows || '<tr><td colspan="5">無</td></tr>'}</tbody>
                           </table>
                           <h2>回傳格式</h2>
                           <table>
                             <thead><tr><th>Status</th><th>Description</th><th>Schema</th></tr></thead>
                             <tbody>${responseRows || '<tr><td colspan="3">無</td></tr>'}</tbody>
                           </table>
                         </body>
                         </html>
                       `;

                       const popup = window.open('', '_blank');
                       if (!popup) {
                         alert('瀏覽器阻擋新分頁，請允許 popup 後重試。');
                         return;
                       }

                       popup.document.open();
                       popup.document.write(html);
                       popup.document.close();
                     };
                   </script>
                 </body>
                 </html>
                 """;
    }

    private static string RenderApiRows(DashboardMetadata metadata)
    {
        var sb = new StringBuilder();
        for (var specIndex = 0; specIndex < metadata.ApiSpecs.Count; specIndex++)
        {
            var spec = metadata.ApiSpecs[specIndex];
            for (var endpointIndex = 0; endpointIndex < spec.Endpoints.Count; endpointIndex++)
            {
                var endpoint = spec.Endpoints[endpointIndex];
                var searchable = $"{endpoint.Method} {endpoint.Path} {endpoint.Summary} {endpoint.TraditionalChineseUsage} {endpoint.SourceHint}";
                sb.Append("<tr class=\"api-row\" data-search=\"")
                    .Append(Encode(searchable))
                    .Append("\">")
                    .Append("<td>").Append(Encode(endpoint.Method)).Append("</td>")
                    .Append("<td>").Append(Encode(endpoint.Path)).Append("</td>")
                    .Append("<td>").Append(Encode(endpoint.Summary)).Append("</td>")
                    .Append("<td>").Append(Encode(endpoint.TraditionalChineseUsage)).Append("</td>")
                    .Append("<td>").Append(Encode(endpoint.SourceHint)).Append("</td>")
                    .Append("<td><button onclick=\"openApiDetail(").Append(specIndex).Append(',').Append(endpointIndex).Append(")\">開啟新分頁</button></td>")
                    .Append("</tr>");
            }
        }

        if (sb.Length == 0)
        {
            return "<tr><td colspan=\"6\">未偵測到 API 規格檔。</td></tr>";
        }

        return sb.ToString();
    }

    private static string RenderRouteRows(IReadOnlyList<RouteInfo> routes)
    {
        if (routes.Count == 0)
        {
            return "<tr><td colspan=\"3\">未偵測到路由。</td></tr>";
        }

        var sb = new StringBuilder();
        foreach (var route in routes)
        {
            var search = $"{route.Method} {route.Path} {route.SourceFile}";
            sb.Append("<tr data-search=\"").Append(Encode(search)).Append("\">")
                .Append("<td>").Append(Encode(route.Method)).Append("</td>")
                .Append("<td>").Append(Encode(route.Path)).Append("</td>")
                .Append("<td>").Append(Encode(route.SourceFile)).Append("</td>")
                .Append("</tr>");
        }

        return sb.ToString();
    }

    private static string RenderDependencyRows(IReadOnlyList<DependencyInfo> dependencies)
    {
        if (dependencies.Count == 0)
        {
            return "<tr><td colspan=\"4\">未偵測到依賴資訊。</td></tr>";
        }

        var sb = new StringBuilder();
        foreach (var dependency in dependencies)
        {
            var search = $"{dependency.Ecosystem} {dependency.Name} {dependency.Version} {dependency.SourceFile}";
            sb.Append("<tr data-search=\"").Append(Encode(search)).Append("\">")
                .Append("<td>").Append(Encode(dependency.Ecosystem)).Append("</td>")
                .Append("<td>").Append(Encode(dependency.Name)).Append("</td>")
                .Append("<td>").Append(Encode(dependency.Version)).Append("</td>")
                .Append("<td>").Append(Encode(dependency.SourceFile)).Append("</td>")
                .Append("</tr>");
        }

        return sb.ToString();
    }

    private static string RenderSqlRows(IReadOnlyList<string> sqlFiles)
    {
        if (sqlFiles.Count == 0)
        {
            return "<tr><td>未偵測到 SQL 檔案。</td></tr>";
        }

        var sb = new StringBuilder();
        foreach (var sqlFile in sqlFiles)
        {
            sb.Append("<tr data-search=\"").Append(Encode(sqlFile)).Append("\">")
                .Append("<td>").Append(Encode(sqlFile)).Append("</td>")
                .Append("</tr>");
        }

        return sb.ToString();
    }

    private static string RenderWorkflowRows(IReadOnlyList<CiWorkflowInfo> workflows)
    {
        if (workflows.Count == 0)
        {
            return "<tr><td colspan=\"3\">未偵測到 CI workflow。</td></tr>";
        }

        var sb = new StringBuilder();
        foreach (var workflow in workflows)
        {
            var search = $"{workflow.Name} {workflow.Trigger} {workflow.RelativePath}";
            sb.Append("<tr data-search=\"").Append(Encode(search)).Append("\">")
                .Append("<td>").Append(Encode(workflow.Name)).Append("</td>")
                .Append("<td>").Append(Encode(workflow.Trigger)).Append("</td>")
                .Append("<td>").Append(Encode(workflow.RelativePath)).Append("</td>")
                .Append("</tr>");
        }

        return sb.ToString();
    }

    private static string RenderTree(TreeNode node)
    {
        if (node.Children.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("<ul>");
        foreach (var child in node.Children)
        {
            sb.Append("<li data-search=\"")
                .Append(Encode(child.Name))
                .Append("\"><span class=\"node ")
                .Append(Encode(child.NodeType))
                .Append("\">")
                .Append(Encode(child.Name))
                .Append("</span>")
                .Append(RenderTree(child))
                .Append("</li>");
        }

        sb.Append("</ul>");
        return sb.ToString();
    }

    private static string RenderSimpleList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "<li>無</li>";
        }

        return string.Join(string.Empty, values.Select(value =>
            $"<li data-search=\"{Encode(value)}\"><code>{Encode(value)}</code></li>"));
    }

    private static string RenderLinks(IReadOnlyList<string> links)
    {
        if (links.Count == 0)
        {
            return "<li>無</li>";
        }

        return string.Join(string.Empty, links.Select(link =>
            $"<li data-search=\"{Encode(link)}\"><a href=\"{Encode(link)}\" target=\"_blank\" rel=\"noreferrer\">{Encode(link)}</a></li>"));
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
