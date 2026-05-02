using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CliWrap;
using CliWrap.Buffered;

namespace DebugAttachService;

/// <summary>
/// Attacher implementation for Visual Studio Code and compatible editors (Cursor, AntiGravity)
/// Creates/updates launch.json with attach configuration and opens the IDE
/// </summary>
public class VSCodeAttacher : IIdeAttacher
{
    private readonly Action<string> _log;
    private readonly Action<string> _logError;
    private int? _f5AttachCheckMaxOverride;

    public VSCodeAttacher(Action<string>? log = null, Action<string>? logError = null)
    {
        _log = log ?? ConsoleLog.WriteLine;
        _logError = logError ?? ConsoleLog.WriteErrorLine;
    }

    public AttachResult Attach(int pid, string idePath, string workspacePath, int? f5AttachCheckMax = null)
    {
        _f5AttachCheckMaxOverride = f5AttachCheckMax;
        try
        {
            // Validate IDE path
            if (string.IsNullOrEmpty(idePath) || !File.Exists(idePath))
            {
                return AttachResult.Fail($"IDE executable not found at: {idePath}");
            }

            // Validate workspace path
            if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
            {
                return AttachResult.Fail($"Workspace path not found: {workspacePath}");
            }

            _log($"[VSCodeAttacher] Workspace path: {workspacePath}");

            var workspaceFolderName = Path.GetFileName(
                workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Create .vscode directory if it doesn't exist
            var vscodePath = Path.Combine(workspacePath, ".vscode");
            Directory.CreateDirectory(vscodePath);

            // Create or update launch.json with attach configuration
            var launchJsonPath = Path.Combine(vscodePath, "launch.json");
            CreateLaunchJson(launchJsonPath, pid);

            _log($"[VSCodeAttacher] Created launch.json at: {launchJsonPath}");

            // Determine which IDE we're using based on the executable name
            var exeName = Path.GetFileNameWithoutExtension(idePath);
            bool isCursor = exeName.Equals("Cursor", StringComparison.OrdinalIgnoreCase);
            bool isAntiGravity = exeName.Equals("Antigravity", StringComparison.OrdinalIgnoreCase);
            string processName = isCursor ? "Cursor" : isAntiGravity ? "Antigravity" : "Code";
            string ideName = isCursor ? "Cursor" : isAntiGravity ? "AntiGravity" : "VS Code";

            // Record current processes before launching
            var existingPids = Process.GetProcessesByName(processName)
                .Select(p => p.Id)
                .ToHashSet();

            // Step 1: Open VS Code/Cursor with the workspace
            var openArgs = $"\"{workspacePath}\" --reuse-window";
            _log($"[VSCodeAttacher] Opening workspace: \"{idePath}\" {openArgs}");

            var openProcess = new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = openArgs,
                UseShellExecute = true
            };
            Process.Start(openProcess);

            // Step 2: Wait for VS Code/Cursor to be ready
            _log($"[VSCodeAttacher] Waiting for {ideName} to be ready...");

            // Check if IDE was already running
            bool wasAlreadyRunning = existingPids.Count > 0;

            int waitedMs = 0;
            int maxWaitMs = GetMaxWaitForProcessMs();
            int intervalMs = 500;
            // Cold start: Electron needs much longer before F5 works; warm: shorter.
            int minWaitMs = GetMinIdeProcessWaitMs(wasAlreadyRunning, isCursor);
            Process? ideProcess = null;

            while (waitedMs < maxWaitMs)
            {
                Thread.Sleep(intervalMs);
                waitedMs += intervalMs;

                // Check if there's a matching process running
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    // Prefer a new process, otherwise use any existing one
                    ideProcess = processes
                        .FirstOrDefault(p => !existingPids.Contains(p.Id))
                        ?? processes.First();

                    // Wait enough time for IDE to fully load the workspace
                    if (waitedMs >= minWaitMs)
                    {
                        _log($"[VSCodeAttacher] {ideName} process found after {waitedMs}ms (PID: {ideProcess.Id}, was running: {wasAlreadyRunning})");
                        break;
                    }
                }
            }

            if (ideProcess == null)
            {
                _logError($"[VSCodeAttacher] {ideName} process not found after waiting");
                _log($"[VSCodeAttacher] Please press F5 in {ideName} manually to start debugging.");
                return AttachResult.Ok();
            }

            // Step 2b: Wait until title or workspace.json indicates the folder opened, then settle (no public "ready for F5" API).
            WaitForIdeReadyOrTimeout(
                processName,
                workspacePath,
                workspaceFolderName,
                ideName,
                wasAlreadyRunning,
                isCursor);

            // Step 3: Start debugging — SendKeys F5 (reliable on Windows); optional experimental CLI if env set
            if (OperatingSystem.IsWindows())
            {
                SendDebugStartAsync(idePath, workspacePath, workspaceFolderName, ideProcess, ideName, processName, pid)
                    .GetAwaiter().GetResult();
            }
            else
            {
                _log($"[VSCodeAttacher] Automatic debug start not supported on this platform.");
                _log($"[VSCodeAttacher] Please press F5 in {ideName} manually to start debugging.");
            }

            return AttachResult.Ok();
        }
        catch (Exception ex)
        {
            _logError($"[VSCodeAttacher] Exception: {ex.Message}");
            return AttachResult.Fail($"Exception: {ex.Message}");
        }
        finally
        {
            _f5AttachCheckMaxOverride = null;
        }
    }

    /// <summary>
    /// SendKeys F5 after focusing the IDE — the reliable way to start attach. Optional <c>DEBUG_ATTACH_TRY_CLI_DEBUG_START=1</c>
    /// runs a non-functional-on-most-builds <c>--command</c> attempt first (extra noise / delay). Skip F5 with <c>DEBUG_ATTACH_SKIP_F5_FALLBACK=1</c>.
    /// </summary>
    private async Task SendDebugStartAsync(
        string idePath,
        string workspacePath,
        string workspaceFolderName,
        Process ideProcess,
        string ideName,
        string processName,
        int gamePid)
    {
        var tryCliExperiment = string.Equals(
            Environment.GetEnvironmentVariable("DEBUG_ATTACH_TRY_CLI_DEBUG_START"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        if (tryCliExperiment)
        {
            var cliArgs = $"\"{workspacePath}\" -r --command=workbench.action.debug.start";
            _log("[VSCodeAttacher] Experimental: CLI --command (often ignored by Electron; attach still needs F5 below):");
            _log($"[VSCodeAttacher]   {idePath} {cliArgs}");

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = idePath,
                        Arguments = cliArgs,
                        UseShellExecute = true
                    });
                    _log($"[VSCodeAttacher]   OK — CLI attempt {attempt}/2 dispatched to {ideName}.");
                }
                catch (Exception ex)
                {
                    _log($"[VSCodeAttacher]   FAIL — CLI attempt {attempt}/2: {ex.Message}");
                }

                if (attempt < 2)
                    await Task.Delay(2000);
            }

            _log("[VSCodeAttacher] Experimental CLI block done.");
            await Task.Delay(500);
        }

        var skipF5Fallback = string.Equals(
            Environment.GetEnvironmentVariable("DEBUG_ATTACH_SKIP_F5_FALLBACK"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        if (skipF5Fallback)
        {
            _log(
                "[VSCodeAttacher] SendKeys skipped — DEBUG_ATTACH_SKIP_F5_FALLBACK=1 (attach will usually not start)."
            );
            return;
        }

        var keySpec = GetStartDebugSendKeysSpec(_log);
        _log($"[VSCodeAttacher] SendKeys — starting attach in {ideName} (SendKeys: {keySpec})…");
        await SendF5KeyPressAsync(ideProcess, ideName, processName, workspaceFolderName, keySpec, gamePid);
    }

    /// <summary>
    /// Maps <c>DEBUG_ATTACH_START_DEBUG_KEYS</c> to a <see cref="System.Windows.Forms.SendKeys"/> sequence.
    /// Default <c>{F5}</c>. Whitelist only — must match your VS Code keybinding for Start Debugging.
    /// Examples: <c>{F8}</c>, <c>^{F5}</c> (Ctrl+F5).
    /// </summary>
    private static string GetStartDebugSendKeysSpec(Action<string> log)
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_START_DEBUG_KEYS")?.Trim();
        if (string.IsNullOrEmpty(raw))
            return "{F5}";

        // https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.sendkeys
        // Optional ^ % + then {F1}-{F24} only (no arbitrary PowerShell).
        if (Regex.IsMatch(raw, @"^[\^%\+]*\{F(2[0-4]|1[0-9]|[1-9])\}$", RegexOptions.CultureInvariant))
            return raw;

        log(
            "[VSCodeAttacher] DEBUG_ATTACH_START_DEBUG_KEYS ignored (invalid format); using {F5}. "
            + "Use SendKeys syntax, e.g. {F8} or ^{F5}."
        );
        return "{F5}";
    }

    private async Task SendF5KeyPressAsync(
        Process ideProcess,
        string ideName,
        string processName,
        string workspaceFolderName,
        string sendKeysSpec,
        int gamePid)
    {
        var focusEditor = GetFocusEditorBeforeKeys();

        if (GetF5UntilDebuggerAttachedEnabled()
            && OperatingSystem.IsWindows()
            && gamePid > 0)
        {
            if (RemoteDebuggerProbe.TryQueryDebuggerAttached(gamePid, out var alreadyDebugging, out _)
                && alreadyDebugging)
            {
                _log(
                    $"[VSCodeAttacher] Game PID {gamePid} already has a debugger attached; skipping SendKeys."
                );
                return;
            }

            var preDelay = GetPreSendKeysDelayMs();
            if (preDelay > 0)
            {
                _log(
                    $"[VSCodeAttacher]   Waiting {preDelay}ms before keys (DEBUG_ATTACH_PRE_SENDKEYS_DELAY_MS)…"
                );
                await Task.Delay(preDelay);
            }

            var maxRounds = GetF5AttachVerifyMaxRounds();
            var verifyDelayMs = GetF5AttachVerifyDelayMs();

            _log(
                $"[VSCodeAttacher] Will retry F5 until debugger attaches to game PID {gamePid} "
                    + $"(max {maxRounds} tries, {verifyDelayMs}ms between send and check; "
                    + "DEBUG_ATTACH_F5_UNTIL_ATTACHED / DEBUG_ATTACH_F5_ATTACH_CHECK_*)."
            );

            for (var round = 1; round <= maxRounds; round++)
            {
                try
                {
                    await SendKeysOneRoundAsync(
                        ideProcess,
                        ideName,
                        processName,
                        workspaceFolderName,
                        sendKeysSpec,
                        focusEditor,
                        round,
                        maxRounds);
                }
                catch (Exception ex)
                {
                    _log($"[VSCodeAttacher]   FAIL — SendKeys round {round}: {ex.Message}");
                }

                try
                {
                    using var gp = Process.GetProcessById(gamePid);
                    _ = gp.Id;
                }
                catch
                {
                    _logError($"[VSCodeAttacher] Game PID {gamePid} exited; stopping F5 retries.");
                    return;
                }

                await Task.Delay(verifyDelayMs);

                if (RemoteDebuggerProbe.TryQueryDebuggerAttached(gamePid, out var attached, out var qErr))
                {
                    if (attached)
                    {
                        _log(
                            $"[VSCodeAttacher] Debugger attached to game PID {gamePid} after F5 round {round}/{maxRounds}."
                        );
                        return;
                    }
                }
                else if (!string.IsNullOrEmpty(qErr))
                {
                    _log($"[VSCodeAttacher] Attach verify query failed: {qErr}");
                }

                if (round < maxRounds)
                    _log($"[VSCodeAttacher] No debugger on game PID {gamePid} yet — sending F5 again…");
            }

            _log(
                "[VSCodeAttacher] Max F5 attach attempts reached; debugger may still attach if Cursor was slow. "
                    + "Increase DEBUG_ATTACH_F5_ATTACH_CHECK_MAX or DEBUG_ATTACH_F5_ATTACH_CHECK_DELAY_MS."
            );
            return;
        }

        // Legacy: fixed repeat count without remote-debugger verification (or verify disabled / non-Windows).
        var preDelayLegacy = GetPreSendKeysDelayMs();
        if (preDelayLegacy > 0)
        {
            _log(
                $"[VSCodeAttacher]   Waiting {preDelayLegacy}ms before keys (DEBUG_ATTACH_PRE_SENDKEYS_DELAY_MS)…"
            );
            await Task.Delay(preDelayLegacy);
        }

        var sendCount = GetSendKeysRepeatCount();
        var delayBetweenSends = GetSendKeysDelayMs();

        if (sendCount > 1)
        {
            _log(
                $"[VSCodeAttacher]   Sending {sendCount}x with {delayBetweenSends}ms between "
                    + "(DEBUG_ATTACH_SENDKEYS_COUNT / DEBUG_ATTACH_SENDKEYS_DELAY_MS)."
            );
        }

        for (var i = 1; i <= sendCount; i++)
        {
            try
            {
                await SendKeysOneRoundAsync(
                    ideProcess,
                    ideName,
                    processName,
                    workspaceFolderName,
                    sendKeysSpec,
                    focusEditor,
                    i,
                    sendCount);
            }
            catch (Exception ex)
            {
                _log($"[VSCodeAttacher]   FAIL — SendKeys send {i}: {ex.Message}");
            }

            if (i < sendCount)
                await Task.Delay(delayBetweenSends);
        }

        _log($"[VSCodeAttacher] Finished SendKeys for {ideName}.");
    }

    private async Task SendKeysOneRoundAsync(
        Process ideProcess,
        string ideName,
        string processName,
        string workspaceFolderName,
        string sendKeysSpec,
        bool focusEditor,
        int index,
        int maxIndex)
    {
        _log($"[VSCodeAttacher]   Sending keys to {ideName} ({index}/{maxIndex})…");

        ideProcess.Refresh();

        if (OperatingSystem.IsWindows())
        {
            var foregroundOk = WindowsForeground.TryActivateBestIdeWindow(
                processName,
                workspaceFolderName,
                out _
            );

            if (!foregroundOk)
                foregroundOk = WindowsForeground.TryActivateProcessWindows(ideProcess.Id);

            if (!foregroundOk)
            {
                _logError(
                    "[VSCodeAttacher]   Could not force Cursor/IDE to foreground (Windows foreground rules). "
                        + "Click the Cursor window once, then try attach again."
                );
            }
            else
            {
                _log($"[VSCodeAttacher]   IDE brought to foreground before SendKeys.");
            }

            await Task.Delay(250);
        }

        SendKeysSendWaitSta(focusEditor, sendKeysSpec);
        _log($"[VSCodeAttacher]   OK — keypress {index}/{maxIndex} sent to {ideName}.");
    }

    private static bool GetF5UntilDebuggerAttachedEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_F5_UNTIL_ATTACHED")?.Trim();
        if (string.IsNullOrEmpty(raw))
            return true;
        if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>
    /// When true with F5-until-attached, skip title/workspace.json waits and shorten min IDE delay — attach probe drives success.
    /// </summary>
    private static bool GetMinimalPreF5WaitEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_MINIMAL_PRE_F5_WAIT")?.Trim();
        if (string.IsNullOrEmpty(raw))
            return true;
        if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool UseFastPreF5Path() =>
        GetMinimalPreF5WaitEnabled() && GetF5UntilDebuggerAttachedEnabled();

    private int GetF5AttachVerifyMaxRounds()
    {
        if (_f5AttachCheckMaxOverride is { } fromRequest)
            return Math.Clamp(fromRequest, 1, 100);
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_F5_ATTACH_CHECK_MAX")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var n))
            return Math.Clamp(n, 1, 100);
        return 12;
    }

    private static int GetF5AttachVerifyDelayMs()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_F5_ATTACH_CHECK_DELAY_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 200, 60000);
        return 1800;
    }

    /// <summary>SendKeys requires STA; service entry is MTA.</summary>
    private static void SendKeysSendWaitSta(bool sendCtrl1FocusEditorFirst, string keys)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (sendCtrl1FocusEditorFirst)
                {
                    SendKeys.SendWait("^1");
                    Thread.Sleep(350);
                }

                SendKeys.SendWait(keys);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (thrown != null)
            throw thrown;
    }

    private static int GetSendKeysRepeatCount()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_SENDKEYS_COUNT")?.Trim();
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out var n))
            return 1;
        return Math.Clamp(n, 1, 5);
    }

    private static int GetSendKeysDelayMs()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_SENDKEYS_DELAY_MS")?.Trim();
        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out var ms))
            return 2000;
        return Math.Clamp(ms, 100, 5000);
    }

    private static int GetPreSendKeysDelayMs()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_PRE_SENDKEYS_DELAY_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 0, 30000);
        if (UseFastPreF5Path())
            return 600;
        return 2500;
    }

    private static int GetMaxWaitForProcessMs()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_MAX_WAIT_PROCESS_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 5000, 120000);
        return 60000;
    }

    private static int GetMinIdeProcessWaitMs(bool wasAlreadyRunning, bool isCursor)
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_MIN_IDE_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 1000, 120000);
        if (UseFastPreF5Path())
            return wasAlreadyRunning ? 1500 : (isCursor ? 3500 : 3000);
        if (wasAlreadyRunning)
            return 6000;
        return isCursor ? 14000 : 10000;
    }

    private static int GetIdeTitleWaitMaxMs(bool wasAlreadyRunning, bool isCursor)
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_IDE_TITLE_WAIT_MAX_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 0, 120000);
        if (UseFastPreF5Path())
            return 0;
        if (wasAlreadyRunning)
            return 12000;
        return isCursor ? 45000 : 30000;
    }

    private static int GetPostReadySettleMs(bool wasAlreadyRunning, bool isCursor)
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_POST_READY_SETTLE_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 0, 120000);
        if (UseFastPreF5Path())
            return 0;
        if (wasAlreadyRunning)
            return 2000;
        return isCursor ? 5500 : 4000;
    }

    /// <summary>
    /// Poll until window title contains the workspace folder name <em>or</em> workspace.json registers this folder,
    /// then wait extra time for extension host / UI (there is no supported external API for "F5 will work").
    /// </summary>
    private void WaitForIdeReadyOrTimeout(
        string processName,
        string workspacePath,
        string workspaceFolderName,
        string ideName,
        bool wasAlreadyRunning,
        bool isCursor)
    {
        var maxMs = GetIdeTitleWaitMaxMs(wasAlreadyRunning, isCursor);
        if (maxMs <= 0)
        {
            if (UseFastPreF5Path())
            {
                _log(
                    "[VSCodeAttacher] Skipping IDE ready wait / post-settle "
                        + "(DEBUG_ATTACH_MINIMAL_PRE_F5_WAIT + F5-until-attached); using F5 retries + debugger probe."
                );
            }

            return;
        }

        _log(
            $"[VSCodeAttacher] Waiting up to {maxMs}ms for {ideName} ready signal "
                + "(FileSystemWatcher on workspaceStorage + title poll; "
                + "DEBUG_ATTACH_IDE_TITLE_WAIT_MAX_MS / DEBUG_ATTACH_IDE_READY_POLL_MS)…"
        );

        var pollMs = IdeReadyWait.GetReadyPollIntervalMs();
        var ok = IdeReadyWait.WaitForFirstSignal(
            processName,
            workspacePath,
            workspaceFolderName,
            maxMs,
            _log,
            out _,
            pollMs);

        if (ok)
        {
            var settle = GetPostReadySettleMs(wasAlreadyRunning, isCursor);
            if (settle > 0)
            {
                _log(
                    $"[VSCodeAttacher] Post-ready settle {settle}ms before SendKeys "
                        + "(extension host has no public event; DEBUG_ATTACH_POST_READY_SETTLE_MS)…"
                );
                Thread.Sleep(settle);
            }

            return;
        }

        _log(
            $"[VSCodeAttacher] No ready signal within {maxMs}ms — SendKeys anyway; "
                + "if attach fails, increase DEBUG_ATTACH_IDE_TITLE_WAIT_MAX_MS, DEBUG_ATTACH_POST_READY_SETTLE_MS, "
                + "or DEBUG_ATTACH_MIN_IDE_MS."
        );
    }

    private static bool GetFocusEditorBeforeKeys()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_FOCUS_EDITOR_BEFORE_KEYS")?.Trim();
        if (string.IsNullOrEmpty(raw))
            return true;
        if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void CreateLaunchJson(string launchJsonPath, int pid)
    {
        var launchConfig = new Dictionary<string, object?>
        {
            ["version"] = "0.2.0",
            ["configurations"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = ".NET Attach (Godot)",
                    ["type"] = "coreclr",
                    ["request"] = "attach",
                    ["processId"] = pid,
                    ["justMyCode"] = true
                }
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(launchConfig, options);
        File.WriteAllText(launchJsonPath, json);
    }
}
