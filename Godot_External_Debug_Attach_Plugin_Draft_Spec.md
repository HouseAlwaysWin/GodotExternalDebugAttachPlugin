# Godot External Debug Attach Plugin
## 技術草稿計畫書（Draft Spec）

> 目標：在 Godot 編輯器內提供一個按鈕，一鍵啟動外部編輯器（VS / Rider / VS Code）並自動 Attach 到正在執行的 Godot C# 程序。

---

## 1. 專案目標（Goal）
建立一個 **Godot Editor Plugin**，在 Godot 編輯器內提供一個按鈕：

- **Run + Attach Debug**
  - 啟動 Godot 專案（等同按下 Play）
  - 取得 Godot C# 執行程序 PID
  - 啟動外部 IDE
  - 自動 Attach 到該 PID

### 目標體驗
- 不需要手動找 PID
- 不需要自己點 Attach
- 主要支援 **Godot 4.x Mono / .NET 的 C# 專案**

---

## 2. 使用情境（User Flow）
1. 使用者在 Godot Editor 按下 `▶ Run + Attach Debug`
2. Plugin：
   - Run 專案
   - 偵測 PID
   - 啟動外部 IDE
   - Attach
3. 使用者在 IDE 中的中斷點可直接命中

---

## 3. 非目標（Non-Goals）
- 不自行實作 CLR Debugger / Debug Engine
- 不改寫 Godot 內建 C# Debugger
- 不實作完整 DAP Server
- 不支援 GDScript Debug（先聚焦 C#）

---

## 4. 技術限制與前提（Constraints）
- Godot：4.x（Mono / .NET）
- Plugin：EditorPlugin
- 作業系統：Windows 優先（Linux / macOS 可延後）
- Plugin 語言：GDScript（主）/ C#（可選，視 Godot 支援情況）

---

## 5. 架構概覽（High-Level Architecture）
```
Godot Editor
  [Plugin Button]
        |
        v
  EditorPlugin (UI / Orchestration)
        |
        v
OS Process Layer (Run / PID discovery)
        |
        v
External IDE Launcher (Rider/VS/VS Code attach)
```

---

## 6. 核心功能模組（Modules）

### 6.1 Editor UI 模組
- Toolbar / Menu：新增 `Run + Attach Debug`
- 設定頁（EditorSettings / ProjectSettings）：
  - External IDE 類型：Rider / Visual Studio / VS Code
  - IDE 執行路徑
  - Attach delay（ms）
  - Auto-run（可選）

### 6.2 Godot Run Control 模組
- 執行專案（等同按 Play）
- 監聽 run 狀態 / 失敗狀態（若可）
- 確認專案啟用 C#（Mono）

### 6.3 PID 偵測模組（關鍵）
目標：取得「實際執行 C# 的 Godot 程序 PID」

**策略 A（推薦）**：掃描系統 Process，比對
- process name：`Godot.exe` / `GodotSharp.exe`（依平台）
- command line 含專案路徑 / scene
- start time 接近按下 Play 的時間點

**策略 B**：若 plugin 可取得子程序資訊，直接記錄 child process

輸出：`PID = 12345`

### 6.4 外部編輯器啟動與 Attach 模組
依 IDE 類型提供不同 attach 策略（以 CLI 優先）：

**Rider**
- Rider CLI 或 Toolbox command（範例）：
  - `rider64.exe attach-to-process --pid 12345`（指令僅示意，需依實際 Rider CLI 校正）

**Visual Studio**
- 可能路徑：vsjitdebugger / EnvDTE / VsWhere + automation（較進階）

**VS Code**
- `code <workspace>` 啟動
- 使用 launch.json + processId / pickProcess / pipeTransport（視方案）

### 6.5 Attach 同步控制
- 避免 attach 太早：提供 `AttachDelayMs`（例如 500~1500ms）
- Attach 成功顯示通知（Toast / Dialog）
- 失敗顯示錯誤與建議（包含 PID、IDE 路徑、CLI stderr）

---

## 7. 設定項目（Settings）
```yaml
ExternalEditor:
  Type: Rider | VisualStudio | VSCode
  ExecutablePath: "C:\Program Files\..."
  AttachMode: Auto
  AttachDelayMs: 1000
  AutoRunOnPlay: false
```

---

## 8. 失敗處理（Error Handling）
- 找不到 PID
- 外部 IDE 未安裝或路徑錯誤
- Attach 失敗（權限、版本、debugger 未啟用）
- 專案不是 C# 專案

輸出方式：
- Godot Editor 通知（Toast / Dialog）
- Editor Console log（包含完整命令列與 stderr）

---

## 9. 里程碑（Milestones）

### Phase 1 – MVP
- Windows only
- 支援 Rider（或先選 VS Code 二擇一）
- 手動按鈕觸發
- 固定 attach delay

### Phase 2 – 擴充
- 支援 VS Code / Visual Studio
- 設定 UI 完整化
- PID 偵測更穩定（多 instance / 多專案路徑）

### Phase 3 – 進階
- Run + Attach 完全一鍵化（包含 focus IDE）
- 自動重連 / 重啟偵測
- 多 instance 支援

---

## 10. 技術風險（Risks）
- Godot Editor API 無法直接取得 child PID → 必須掃描系統 process
- IDE attach CLI / automation 不同版本差異大
- Windows 權限 / UAC 造成 attach 失敗
- Godot 更新造成程序名稱/參數改動

---

## 11. 交付成果（Deliverables）
- Plugin source
- README（安裝、設定、常見錯誤）
- 範例 Godot C# 專案（用來測 attach）
- IDE attach 命令列範本（各 IDE 一份）

---

## 12. 延伸（Optional）
- Play Current Scene + Attach
- IDE 快速切換
- Attach 成功後自動 focus IDE
- 未來再考慮 DAP 更深度整合
