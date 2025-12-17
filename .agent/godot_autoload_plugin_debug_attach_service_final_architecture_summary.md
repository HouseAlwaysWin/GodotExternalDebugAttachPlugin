# Godot Autoload + Plugin + Debug Attach Service
## Final Architecture Summary (for Antigravity)

> **本文件目的**：
> 將最終確認的架構與規則「一次講清楚」，避免實作時走偏。
> 本文件可直接作為 **唯一實作依據**。

---

## 1. 核心結論（一句話版）

> **Autoload 不負責 Debug，只負責確保 Service 存在；**  
> **Plugin 決定「什麼時候要 Debug」；**  
> **Debug Attach Service 負責「怎麼 Attach 編輯器 / Debugger」。**

只要嚴格遵守這三句話，系統就不會踩到 Godot C# Debug 的已知大坑。

---

## 2. 為什麼要這樣設計

- Godot C# 在啟動初期 attach debugger 會有 race condition（例如 godotengine/godot#78513）
- 問題核心不是 debugger，而是 **attach 時序不可控**

**解法原則**：
- ❌ 不讓 debugger 猜時機
- ✅ 由 Plugin 在「安全時機」明確通知

---

## 3. 最終系統架構

```
Godot
├─ Autoload
│    └─ Ensure Debug Attach Service Running
│        （只啟動 / 確保存在）
│
├─ Plugin（使用者操作）
│    └─ 使用者按下 Debug 按鈕
│         └─ Plugin 判定 DebugReady
│              └─ 傳送訊號（含 PID）
│
└─ Debug Attach Service（獨立程式）
     ├─ 常駐監聽 IPC
     ├─ 收到 Plugin 訊號
     └─ 啟動編輯器 / Debugger 做 attach
```

---

## 4. 各角色責任（不可違反）

### 4.1 Autoload（Godot）

**唯一責任**：
- 確保 Debug Attach Service 已啟動並在監聽

**Autoload 絕對不能做的事**：
- ❌ 不取得 PID
- ❌ 不送 DebugReady
- ❌ 不決定 attach 時機
- ❌ 不啟動編輯器 / debugger
- ❌ 不知道任何 debug 細節

> Autoload 只是「工具 bootstrapper」，不是 debug controller。

---

### 4.2 Plugin（Godot Editor Plugin）

**唯一合法的 Debug 觸發來源**。

Plugin 要做的事：
- 接受使用者操作（Debug 按鈕）
- 在安全時機判定 DebugReady
- 取得 PID（OS.GetProcessId）
- 將 Debug 指令（含 PID）送給 Service

Plugin 禁止：
- ❌ 自動 debug（必須是使用者觸發）
- ❌ 啟動 Service
- ❌ 啟動編輯器

---

### 4.3 Debug Attach Service（Command Line 程式）

**獨立常駐程式（daemon / CLI）**。

Service 要做的事：
- 啟動 IPC 監聽（Pipe / TCP）
- 接收 Plugin 發送的 Debug 指令
- 驗證 PID 是否存在
- 控制 attach 行為（延遲 / retry）
- 啟動編輯器（VS Code）或 debugger 進行 attach

Service 不要做的事：
- ❌ 不解析 C# 程式碼
- ❌ 不知道 Godot 專案結構
- ❌ 不主動 attach（一定是被 Plugin 叫）

---

## 5. Debug 時序規則（非常重要）

### 5.1 PID 與 DebugReady 必須分離

- PID 可以在 Plugin 很早期取得
- **但 attach 只能在使用者按下 Debug 後發生**

> 危險的不是 PID 早，而是 attach 早。

---

### 5.2 為什麼這樣不會踩 Godot Bug

- attach 不是在 Godot 啟動時自動發生
- attach 是「明確、晚發生、可預期」的行為
- 避開 CoreCLR / Script 尚未 ready 的時段

---

## 6. Plugin → Service 訊息格式（範例）

```json
{
  "type": "debug-attach-request",
  "pid": 18324,
  "engine": "godot",
  "editor": "vscode",
  "timestamp": "2025-01-16T12:34:56Z"
}
```

- `pid` 是 attach 的唯一依據
- 是否用 VS Code / 其他 debugger 由 Service 決定

---

## 7. Debug Attach Service 行為流程

```
Idle
 └─ 收到 DebugAttachRequest
     └─ 驗證 PID
         └─ 嘗試 Attach
             ├─ 成功 → 完成
             └─ 失敗 → 延遲 + retry（最多 N 次）
```

---

## 8. 實作硬性規則（請 AI 嚴格遵守）

1. Autoload 永遠不參與 debug 決策
2. Debug 一定是 Plugin 的顯式行為
3. Service 永遠不主動 attach
4. attach 時序一定晚於 Godot 啟動
5. Service crash 不影響 Godot

---

## 9. 成功判定（Acceptance Criteria）

- 進入 Godot 專案時 Service 已在監聽
- 使用者按下 Plugin Debug 按鈕
- 編輯器 / debugger 自動 attach
- breakpoint 可正常命中
- 不需重啟 Godot

---

## 10. 最重要的一句話（給 AI）

> **這個系統的重點不是「怎麼 debug」，
> 而是「attach 只能在正確時機發生」。**
>
> 任何自動、過早的 attach 都視為 bug。

---

**本文件即為最終架構定義，不得自行擴權或簡化角色責任。**

