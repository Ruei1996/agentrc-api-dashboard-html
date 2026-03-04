# macOS 安裝 SOP

## 1) 安裝必要工具

```bash
# Homebrew (若尚未安裝)
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# Git / .NET / Python
brew install git
brew install --cask dotnet-sdk
brew install python
```

## 2) 設定 .NET

```bash
dotnet --version
```

若找不到 `dotnet`，重開 terminal 或將 `/usr/local/share/dotnet` 加入 PATH。

## 3) 還原與建置

```bash
cd /path/to/agentrc-api-dashboard-html
dotnet restore
dotnet build agentrc-api-dashboard-html.slnx
```

## 4) 執行掃描

```bash
dotnet run --project src/AgentrcApiDashboard -- \
  --target /path/to/target-repo \
  --output-root ~/Downloads \
  --result-dir api-dashboard-result \
  --use-copilot \
  --copilot-model gpt-5-mini
```

## 5) (可選) HTML runtime 驗證工具

```bash
pip3 install --user playwright
python3 -m playwright install chromium
```

