# AgentRC API Dashboard HTML (C#)

以 `microsoft/agentrc` 的「CLI + Service」風格為基礎，這個專案會掃描指定 repo，產生離線可讀取的：

- `{repo}-dashboard-meta-data-{branch}.json`
- `{repo}-dashboard-{branch}.html`

## 快速開始

```bash
dotnet run --project src/AgentrcApiDashboard -- \
  --target /path/to/repo \
  --output-root ~/Downloads \
  --result-dir api-dashboard-result \
  --use-copilot \
  --copilot-model gpt-5-mini
```

## 主要功能

- 掃描 `.env`、`.gitignore`、`.github/copilot-instructions.md`（`.env` 永遠納入）
- 解析 `.gitignore` 規則避免誤掃描
- 自動偵測 Swagger/OpenAPI (`*swagger*.json`, `*openapi*.json`)
- 輸出 API、依賴、樹狀圖、CI/CD、Build/Run/Test 指南
- 產生可搜尋、可篩選、可開新分頁看 API 詳細資料的 dashboard
- 支援手動掃描與 `--interval-minutes` 週期重掃

## 文件

- SDD: `docs/sdd/`
- macOS 安裝 SOP: `docs/setup-macos.md`
- Git profile 切換與推送: `docs/git-config-switching.md`

