# SDD-03 更新與驗證規格

## 更新模式
- 手動更新：直接執行 CLI 一次掃描一次輸出。
- 週期更新：`--interval-minutes <n>` 持續重掃，直到 `Ctrl+C`。
- 指定分支：
  - `--checkout-branch`：掃描前先 checkout
  - `--branch`：只覆蓋輸出檔名 branch（不切分支）

## Copilot SDK 繁中化
- 透過 `GitHub.Copilot.SDK` session 生成 endpoint `usageZh + flowStepsZh`。
- 權限策略：`OnPermissionRequest = PermissionHandler.ApproveAll`。
- 當 SDK 不可用時 fallback 規則式敘述，且寫入 warning。

## 驗證規範
- Build 驗證：`dotnet build`
- 實掃驗證：指定真實 repo 路徑執行掃描
- 瀏覽器驗證：
  - headless 開啟輸出 HTML
  - 檢查 `pageerror` 與 `console error` 為 0

