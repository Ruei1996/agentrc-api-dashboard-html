# SDD-01 Repo 掃描規格

## 必掃與忽略規則
- 必掃：
  - `.env`
  - `.gitignore`
  - `.github/copilot-instructions.md`
- 其餘檔案依 `.gitignore` 規則過濾。
- 若 `git check-ignore` 可用，優先使用 git 原生行為；不可用時 fallback 內建 matcher。

## 語言與內容偵測
- 語言統計：副檔名映射（Go/C#/TS/JS/Python/SQL...）
- 依賴解析：
  - Go：`go.mod`
  - .NET：`*.csproj`
  - Node：`package.json`
  - Python：`requirements.txt`
  - Docker：`Dockerfile*`
- 路由解析（regex-based）：
  - Go router / `HandleFunc`
  - ASP.NET `MapGet/MapPost/...`
  - Annotation 風格 `HttpGet/HttpPost/...`
- SQL：蒐集 `*.sql` 路徑。

## Swagger/OpenAPI 偵測
- 規則：`*swagger*.json` 或 `*openapi*.json`
- 優先覆蓋 `doc/swagger/service.swagger.json` 這類路徑。
- 解析內容：
  - Method/Path
  - Summary/Description
  - Parameters
  - Responses
  - Source hint（嘗試與 route 對映）

