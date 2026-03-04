# SDD-00 產品規格總覽

## 目標
建立一個以單一 repo 為單位的離線 dashboard 產生器，讓開發者快速理解專案架構、依賴、API、SQL、路由與 CI/CD。

## 輸入/輸出
- 輸入：`--target <repo path>`
- 預設輸出根目錄：作業系統 Downloads（可覆寫）
- 輸出檔案：
  - `{repo}-dashboard-meta-data-{branch}.json`
  - `{repo}-dashboard-{branch}.html`

## 架構 (AgentRC-style)
- CLI 層：參數解析、流程控制 (`Cli/OptionParser.cs`)
- Service 層：掃描、解析、翻譯、渲染、輸出 (`Services/*.cs`)
- Model 層：metadata 結構 (`Models/DashboardMetadata.cs`)

## 驗收條件
1. 可掃描真實 repo，不能使用假資料。
2. `.gitignore` 可生效，但 `.env` 必須永遠納入。
3. HTML 與 JSON 皆可離線閱讀。
4. HTML 支援搜尋/篩選與 API detail 新分頁。

