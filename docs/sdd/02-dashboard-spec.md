# SDD-02 Dashboard 規格

## UI 區塊
- Header：專案名稱、branch、生成時間、最後 commit 時間
- Sidebar：區塊導覽（總覽/依賴/API/路由/SQL/CI/指南）
- Footer：離線模式提示

## 核心互動
- 全域搜尋框：同時過濾 API/路由/SQL/依賴/CI rows
- API 詳細頁：
  - 主頁點擊按鈕另開新分頁
  - 顯示繁中用法、流程、參數、回應格式
- 樹狀圖：目錄節點與截斷提示

## 資料來源
- dashboard 內嵌 metadata JSON（`<script type="application/json">`）
- 完全離線，不依賴遠端 API。

## 命名規則
- JSON：`{repo}-dashboard-meta-data-{branch}.json`
- HTML：`{repo}-dashboard-{branch}.html`
- branch/repo 名稱以安全 slug 輸出。

