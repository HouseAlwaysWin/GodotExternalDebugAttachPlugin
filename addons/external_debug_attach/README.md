# External Debug Attach Plugin

一鍵 Run + Attach Debug 到外部 IDE（Rider / VS Code）的 Godot Editor Plugin。

## 安裝

1. 將 `addons/external_debug_attach/` 資料夾複製到您的 Godot 專案
2. 重新建置 C# 專案（確保 plugin 編譯成功）
3. 在 Godot Editor 中：Project → Project Settings → Plugins
4. 啟用 "External Debug Attach" plugin

## 設定

在 Editor → Editor Settings 中找到 "External Debug Attach" 設定：

| 設定項 | 說明 |
|--------|------|
| IDE Type | 選擇 IDE 類型：Rider 或 VSCode |
| IDE Path | IDE 可執行檔路徑（留空自動偵測） |
| Attach Delay Ms | Attach 前的等待時間（毫秒） |
| Solution Path | .sln 檔案路徑（留空自動偵測） |

## 使用方法

1. 確認設定正確
2. 在 Godot Editor 的 toolbar 點擊 **▶ Run + Attach Debug**
3. Plugin 會自動：
   - 執行專案
   - 偵測 Godot 遊戲程序 PID
   - 啟動 IDE 並附加 debugger

## IDE 支援

### Rider
- 使用 `rider attach-to-process netcore <pid> <solution>` 命令
- 需要 Rider 2020.1 或更新版本

### VS Code
- 自動生成 `.vscode/launch.json`
- 需要安裝 C# 擴充套件
- 開啟 VS Code 後需手動啟動 debug 配置

## 常見問題

### 找不到 PID
- 確認專案已使用 C# 建置
- 增加 Attach Delay 時間

### Rider 無法附加
- 確認 Rider 路徑正確
- 確認 .sln 路徑正確
- 查看 Godot 編輯器 Console 的錯誤訊息

### VS Code 無法附加
- 確認已安裝 C# 擴充套件
- 在 VS Code 中手動選擇 ".NET Attach (Godot)" 配置
