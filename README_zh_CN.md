# External Debug Attach Plugin

[English](README.md) | **中文**

一鍵 Run + Attach Debug 到外部 IDE（VS Code）的 Godot Editor Plugin。

## 特色

- 🚀 一鍵執行遊戲並附加 Debugger
- 🔧 支援 VS Code
- ⏳ 可選的等待 Debugger 功能（確保不錯過初始化斷點）
- 🎯 自動偵測 IDE 和 Solution 路徑
- ⌨️ 快捷鍵支援：**Alt+F5**

## 安裝

1. 將 `addons/external_debug_attach/` 資料夾複製到您的 Godot 專案
2. 重新建置 C# 專案（確保 plugin 編譯成功）
3. 在 Godot Editor 中：Project → Project Settings → Plugins
4. 啟用 "External Debug Attach" plugin

## 設定

在 Editor → Editor Settings 中找到 "External Debug Attach" 設定：

| 設定項 | 說明 |
|--------|------|
| IDE Path | IDE 可執行檔路徑（留空自動偵測） |
| Attach Delay Ms | Attach 前的等待時間（毫秒） |

## 使用方法

1. 確認設定正確
2. 在 Godot Editor 的 toolbar 點擊 **🐞 Run + Attach Debug** (或按 `Alt+F5`)
3. Plugin 會自動：
   - 執行專案
   - 偵測 Godot 遊戲程序 PID
   - 啟動 IDE 並附加 debugger

## 等待 Debugger 附加（可選）

為確保不會錯過初始化時的斷點（如 `_Ready`），plugin 會在啟用時自動註冊 Autoload：

- **DebugWait** (`addons/external_debug_attach/DebugWaitAutoload.cs`)

啟用 Plugin 後：
- 遊戲啟動時會暫停並顯示「Waiting for debugger...」
- Debugger 附加後自動繼續
- 按 ESC 可跳過等待
- 超時 30 秒後自動繼續

## IDE 支援

### VS Code
- 自動生成 `.vscode/launch.json`
- 需要安裝 C# 擴充套件
- 自動發送 F5 開始除錯

## 常見問題

### 找不到 PID
- 確認專案已使用 C# 建置
- 增加 Attach Delay 時間

### VS Code 無法附加
- 確認已安裝 C# 擴充套件
- 在 VS Code 中手動選擇 ".NET Attach (Godot)" 配置

## 已知限制

- **Debug Sesssion 結束後需重啟 Godot**：由於 [Godot #78513](https://github.com/godotengine/godot/issues/78513) bug，.NET assembly 重載可能會失敗，導致下次 debug 時報錯。Plugin 會在偵測到錯誤時跳出提醒視窗。
- **僅支援 Windows**：目前使用 WMI 進行程序偵測，僅支援 Windows 平台。
