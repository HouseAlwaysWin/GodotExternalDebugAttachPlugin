# Godot C# Debug Attach Service – Implementation Guide

> **目標讀者**：Antigravity（或任何自動實作型 AI）
>
> **目的**：實作一套「可控時序」的 Godot C# Debug Attach 系統，
> 解決 Godot C# 在過早 attach 時造成的 debugger 失效問題（如 godotengine/godot#78513）。

---

## 1. 問題背景（為什麼要做這件事）

- Godot 4.x 的 C#（CoreCLR）專案，在 **啟動初期 attach debugger** 時，
  可能因為 CoreCLR / Script 尚未完全 ready，導致：
  - breakpoint 永遠 pending
  - debug session 直接失效
- 這是 **Godot 啟動時序（race condition）問題**，不是 debugger 本身錯誤。

**核心原則**：
> ❌ debugger 不應該自己猜何時 attach  
> ✅ Godot 本身最清楚「什麼時候可以被 debug」

---

## 2. 解決方案總覽（一句話）

> **由 Godot Plugin 在「安全時機」送出 DebugReady 訊號，
> 再由外部常駐服務負責 attach debugger。**

---

## 3. 系統架構總覽

```
+------------------+
| Godot (.NET)     |
|  Debug Plugin    |
|------------------|
| - 很早取得 PID   |
| - 等待 CLR Ready |
| - 等待 Script OK |
| - 發送 DebugReady|
+---------+--------+
          |
          | IPC (Pipe / TCP)
          v
+---------------------------+
| Debug Attach Service (CLI)|
|---------------------------|
| - 常駐監聽                |
| - 驗證 PID                |
| - 控制 attach 時序        |
| - 呼叫 debugger attach   |
+---------+-----------------+
          |
          v
+---------------------------+
| Debugger                  |
| - netcoredbg (優先)       |
| - VS Code attach (備用)   |
+---------------------------+
```

---

## 4. 角色與責任切分（非常重要）

### 4.1 Godot Debug Plugin（C# / EditorPlugin）

**只負責「宣告狀態」，不負責 debug 行為。**

#### Plugin 要做的事
- 在 Plugin 初始化時即可取得 **PID**（OS.GetProcessId）
- 等待「可以安全 debug」的時機
- 對外送出 `DebugReady(pid)` 訊息

#### Plugin 絕對不能做的事
- ❌ 不啟動 VS Code
- ❌ 不啟動 netcoredbg
- ❌ 不處理 breakpoint / debug protocol

> Plugin 不應知道任何 IDE 或 debugger 的存在

---

### 4.2 Debug Attach Service（Command Line 程式）

**這是一個長時間執行的 CLI / daemon 程式。**

#### Service 要做的事
- 啟動時建立 IPC 監聽（Pipe / TCP）
- 接收 Godot Plugin 傳來的 `DebugReady` 訊息
- 驗證 PID 是否存在、是否為 Godot
- 控制 attach 時機（延遲 / retry）
- 呼叫 debugger 進行 attach

#### Service 不要做的事
- ❌ 不解析 C# 程式碼
- ❌ 不顯示 UI（第一版）
- ❌ 不直接操作 Godot

---

## 5. 關鍵設計原則（請嚴格遵守）

### 5.1 PID 與 DebugReady 必須分離

- **PID 可以非常早取得**（Plugin 啟動時）
- **但 DebugReady 絕對不能太早送出**

> 危險的不是「PID 早」，而是「attach 早」

---

### 5.2 Plugin 啟動 ≠ Debug Ready

以下行為 **禁止送出 DebugReady**：
- `_enter_tree`
- Plugin 剛載入
- Godot Editor 剛打開

DebugReady 只能在以下條件之一成立後送出：
- 第一個「非 Plugin」的 C# Script 已載入
- Scene / Play 開始後
- 或延遲 ≥ 500–1000ms（實務保險作法）

---

## 6. IPC 規格（Godot → Service）

### 6.1 通訊方式

- Windows：Named Pipe（優先）
- Linux / macOS：Unix Domain Socket
- 備用：127.0.0.1 TCP

### 6.2 訊息格式（JSON）

```json
{
  "type": "godot-debug-ready",
  "pid": 18324,
  "engine": "godot",
  "runtime": "dotnet",
  "tfm": "net6.0",
  "timestamp": "2025-01-16T12:34:56Z"
}
```

**pid 是 attach 的唯一依據，不可省略。**

---

## 7. Debug Attach Service 行為規格

### 7.1 基本流程

```
Idle
 └─ 收到 DebugReady
     └─ 驗證 PID
         └─ 嘗試 Attach #1
             ├─ 成功 → Attached
             └─ 失敗
                 └─ 延遲 300ms
                     └─ Retry（最多 3 次）
```

### 7.2 Attach 規則

- attach 只能在收到 DebugReady 後執行
- attach 失敗必須 retry（Godot race condition）
- attach 成功後才允許 breakpoint resolve

---

## 8. Debugger 使用規格

### 8.1 netcoredbg（正式方案）

DAP attach 範例：

```json
{
  "type": "coreclr",
  "request": "attach",
  "processId": 18324,
  "justMyCode": false
}
```

### 8.2 VS Code Attach（備用 / 過渡）

- 僅用於驗證整條 pipeline
- 不視為最終架構
- 啟動行為由 Service 控制，不可由 Plugin 控制

---

## 9. CLI 程式建議結構

```
DebugAttachService
├─ Program.cs
├─ Listener
│   ├─ PipeListener
│   └─ TcpListener
├─ Protocol
│   └─ DebugReadyMessage
├─ Attach
│   ├─ IAttachStrategy
│   ├─ NetCoreDbgAttach
│   └─ VSCodeAttach (optional)
├─ State
│   └─ AttachStateMachine
└─ Logging
```

---

## 10. 實作順序建議（給 AI）

### Phase 1
- Godot Plugin：送 DebugReady
- Debug Attach Service：接收 + log
- VS Code attach 驗證

### Phase 2
- netcoredbg attach
- retry / state machine

### Phase 3
- 與 YavaPad 整合
- UI 顯示 debug 狀態（非本文件範圍）

---

## 11. 成功定義（Acceptance Criteria）

- Godot 手動啟動
- Plugin 在安全時機送出 DebugReady
- Service 自動 attach debugger
- breakpoint 可命中
- 不需重啟 Godot

---

## 12. 最重要的實作提醒（給 AI）

> **Debug attach 的關鍵不是 debugger 技術，
> 而是「attach 的時序控制」。**
>
> 任何過早 attach 都視為 bug。

---

**本文件為唯一實作依據。**

