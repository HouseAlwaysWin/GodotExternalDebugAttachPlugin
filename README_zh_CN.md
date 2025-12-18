# External Debug Attach Plugin

[English](README.md) | **中文**

一鍵 Run + Attach Debug 到外部 IDE 的 Godot Editor Plugin。

## 架構

本 Plugin 採用**雙組件架構**以避免 C# assembly reload 問題：

| 組件                     | 語言     | 說明                                              |
| ------------------------ | -------- | ------------------------------------------------- |
| **Editor Plugin**        | GDScript | 處理 UI、設定和通訊                               |
| **Debug Attach Service** | .NET 8   | 獨立程序，負責 PID 偵測、IDE 啟動和 debugger 附加 |

## 特色

- 🚀 一鍵執行遊戲並附加 Debugger
- 🔧 支援 **VS Code**、**Cursor** 和 **AntiGravity**
- ⏳ 內建等待 Debugger 功能（確保不錯過 `_Ready` 斷點）
- 🎯 自動偵測 IDE 路徑和遊戲程序 PID
- ⌨️ 快捷鍵：**Alt+F5**
- 🖥️ 可選的 Service 主控台視窗（用於除錯）

## 安裝

1. 將 `addons/external_debug_attach/` 資料夾複製到您的 Godot 專案
2. 重新建置 C# 專案（確保 plugin 編譯成功）
3. 在 Godot Editor 中：Project → Project Settings → Plugins
4. 啟用 "External Debug Attach" plugin

## 設定

在 Editor → Editor Settings 中找到 "External Debug Attach" 設定：

| 設定項               | 說明                                     |
| -------------------- | ---------------------------------------- |
| IDE Type             | 選擇 IDE：VSCode、Cursor 或 AntiGravity  |
| VS Code Path         | VS Code 可執行檔路徑（留空自動偵測）     |
| Cursor Path          | Cursor 可執行檔路徑（留空自動偵測）      |
| AntiGravity Path     | AntiGravity 可執行檔路徑（留空自動偵測） |
| Show Service Console | 顯示 Debug Attach Service 主控台視窗     |

### 顯示 Service 主控台視窗

如果您想查看 Debug Attach Service 的執行日誌（用於除錯問題），可以啟用主控台視窗：

1. 進入 **Editor** → **Editor Settings**
2. 搜尋 **External Debug Attach**
3. 將 **Show Service Console** 設為 **true**

啟用後，每次按 Alt+F5 時會彈出一個 CMD 視窗，顯示：

- TCP 伺服器狀態
- 收到的請求內容
- PID 偵測結果
- IDE 啟動狀態
- F5 按鍵發送結果

> **提示**：如果不小心關閉了 Service 視窗，可以透過**停用再啟用 Plugin** 來重新啟動：
>
> 1. 進入 **Project** → **Project Settings** → **Plugins**
> 2. 取消勾選 **External Debug Attach**
> 3. 再次勾選 **External Debug Attach**

## 使用方法

1. 確認設定正確
2. 在 Godot Editor 的 toolbar 點擊 **🐞 Run + Attach Debug** (或按 `Alt+F5`)
3. Plugin 會自動：
   - 啟動 Debug Attach Service（如果尚未運行）
   - 執行專案
   - 暫停遊戲等待 debugger（透過 DebugWaitAutoload）
   - 偵測 Godot 遊戲程序 PID
   - 啟動 IDE 並附加 debugger

## 運作流程

1. **GDScript Plugin** 透過 TCP 發送 attach 請求給 Service
2. **Debug Attach Service**（獨立 .NET 程序）：
   - 掃描 Godot/dotnet 遊戲程序
   - 自動偵測 IDE 安裝路徑
   - 建立/更新 `.vscode/launch.json`
   - 啟動 IDE 開啟工作區
   - 發送 F5 按鍵開始除錯
3. **DebugWaitAutoload**（在遊戲程序中）：
   - 使用視覺覆蓋層暫停遊戲
   - 同步阻塞等待 debugger 附加
   - debugger 連接後自動繼續

## DebugWaitAutoload

Plugin 在啟用時會自動註冊 `DebugWaitAutoload`，確保不會錯過初始化時的斷點（如 `_Ready`）。

遊戲啟動時：

- 顯示「Waiting for debugger...」覆蓋層
- Debugger 附加後自動繼續
- 按 **ESC** 可跳過等待
- 超時 30 秒後自動繼續

## IDE 支援

### VS Code

- 自動生成/更新 `.vscode/launch.json`
- 需要安裝 C# 擴充套件
- 自動發送 F5 開始除錯

### Cursor

- 與 VS Code 相同（使用相同的 Debugger 設定）
- 自動偵測 Cursor 安裝路徑

### AntiGravity

- 與 VS Code 相同（使用相同的 Debugger 設定）
- 自動偵測 AntiGravity 安裝路徑

## 常見問題

### Service 無法啟動

- 檢查是否有其他實例正在執行
- 啟用 **Show Service Console** 查看錯誤訊息
- 手動執行 `StartService.bat` 診斷問題

### 找不到 PID

- 確認專案已使用 C# 建置
- Service 會自動重試最多 10 次

### IDE 無法附加

- 確認 IDE 已安裝 C# 擴充套件
- 手動選擇 ".NET Attach (Godot)" 配置

## 已知限制

- **僅支援 Windows**：目前只支援 Windows 平台

## 授權

MIT License
