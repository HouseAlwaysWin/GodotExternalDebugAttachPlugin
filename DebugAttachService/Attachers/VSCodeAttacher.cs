using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public VSCodeAttacher(Action<string>? log = null, Action<string>? logError = null)
    {
        _log = log ?? Console.WriteLine;
        _logError = logError ?? Console.Error.WriteLine;
    }

    public AttachResult Attach(int pid, string idePath, string workspacePath)
    {
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
            int maxWaitMs = 25000; // Max 25 seconds
            int intervalMs = 500;
            // If IDE was already running, we need to wait for the workspace to reload
            // If IDE is newly started, wait longer for full initialization
            int minWaitMs = wasAlreadyRunning ? 6000 : 8000;
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
                        _log($"[VSCodeAttacher] {ideName} ready after {waitedMs}ms (PID: {ideProcess.Id}, was running: {wasAlreadyRunning})");
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

            // Step 3: Start debugging — SendKeys F5 (reliable on Windows); optional experimental CLI if env set
            if (OperatingSystem.IsWindows())
            {
                SendDebugStartAsync(idePath, workspacePath, ideProcess, ideName, processName).GetAwaiter().GetResult();
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
    }

    /// <summary>
    /// SendKeys F5 after focusing the IDE — the reliable way to start attach. Optional <c>DEBUG_ATTACH_TRY_CLI_DEBUG_START=1</c>
    /// runs a non-functional-on-most-builds <c>--command</c> attempt first (extra noise / delay). Skip F5 with <c>DEBUG_ATTACH_SKIP_F5_FALLBACK=1</c>.
    /// </summary>
    private async Task SendDebugStartAsync(string idePath, string workspacePath, Process ideProcess, string ideName, string processName)
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
        await SendF5KeyPressAsync(ideProcess, ideName, processName, keySpec);
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

    private async Task SendF5KeyPressAsync(Process ideProcess, string ideName, string processName, string sendKeysSpec)
    {
        const int sendCount = 1;
        const int delayBetweenSends = 500;

        var psLiteral = sendKeysSpec.Replace("'", "''");

        for (int i = 1; i <= sendCount; i++)
        {
            _log($"[VSCodeAttacher]   Sending keys to {ideName} ({i}/{sendCount})…");

            try
            {
                ideProcess.Refresh();
                if (OperatingSystem.IsWindows())
                {
                    WindowsForeground.TryActivateProcessWindows(ideProcess.Id);
                    WindowsForeground.TryActivateAnyNamedProcess(processName);
                    await Task.Delay(150);
                }

                var psCommand = $"Add-Type -AssemblyName Microsoft.VisualBasic; " +
                    $"[Microsoft.VisualBasic.Interaction]::AppActivate({ideProcess.Id}); " +
                    "Start-Sleep -Milliseconds 250; " +
                    $"[Microsoft.VisualBasic.Interaction]::AppActivate({ideProcess.Id}); " +
                    "Start-Sleep -Milliseconds 250; " +
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    $"[System.Windows.Forms.SendKeys]::SendWait('{psLiteral}')";

                var result = await Cli.Wrap("powershell")
                    .WithArguments(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", psCommand })
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                if (result.ExitCode == 0)
                {
                    _log($"[VSCodeAttacher]   OK — keypress {i}/{sendCount} sent to {ideName}.");
                }
                else
                {
                    _log($"[VSCodeAttacher]   FAIL — PowerShell exit code {result.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(result.StandardError))
                    {
                        _log($"[VSCodeAttacher] Error: {result.StandardError.Trim()}");
                    }
                }
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

    private void CreateLaunchJson(string launchJsonPath, int pid)
    {
        var launchConfig = new
        {
            version = "0.2.0",
            configurations = new[]
            {
                new
                {
                    name = ".NET Attach (Godot)",
                    type = "coreclr",
                    request = "attach",
                    processId = pid.ToString()
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(launchConfig, options);
        File.WriteAllText(launchJsonPath, json);
    }
}
