using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ExternalDebugAttach;

/// <summary>
/// Attacher implementation for Visual Studio Code
/// Creates/updates launch.json with attach configuration and opens VS Code
/// </summary>
public class VSCodeAttacher : IIdeAttacher
{
    public AttachResult Attach(int pid, string idePath, string solutionPath)
    {
        try
        {
            // Validate IDE path
            if (string.IsNullOrEmpty(idePath) || !File.Exists(idePath))
            {
                return AttachResult.Fail($"VS Code executable not found at: {idePath}");
            }

            // Get workspace path - prefer solution directory, fallback to project path
            var projectPath = ProjectSettings.GlobalizePath("res://");
            string workspacePath;

            if (!string.IsNullOrEmpty(solutionPath) && File.Exists(solutionPath))
            {
                workspacePath = Path.GetDirectoryName(solutionPath) ?? projectPath;
            }
            else
            {
                workspacePath = projectPath;
            }

            GD.Print($"[VSCodeAttacher] Workspace path: {workspacePath}");

            if (string.IsNullOrEmpty(workspacePath))
            {
                return AttachResult.Fail("Could not determine workspace path");
            }

            // Create .vscode directory if it doesn't exist
            var vscodePath = Path.Combine(workspacePath, ".vscode");
            Directory.CreateDirectory(vscodePath);

            // Create or update launch.json with attach configuration
            var launchJsonPath = Path.Combine(vscodePath, "launch.json");
            CreateLaunchJson(launchJsonPath, pid);

            GD.Print($"[VSCodeAttacher] Created launch.json at: {launchJsonPath}");

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
            GD.Print($"[VSCodeAttacher] Opening workspace: \"{idePath}\" {openArgs}");

            var openProcess = new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = openArgs,
                UseShellExecute = true
            };
            Process.Start(openProcess);

            // Step 2: Wait for VS Code/Cursor to be ready
            GD.Print($"[VSCodeAttacher] Waiting for {ideName} to be ready...");

            // Check if IDE was already running
            bool wasAlreadyRunning = existingPids.Count > 0;

            int waitedMs = 0;
            int maxWaitMs = 15000; // Max 15 seconds
            int intervalMs = 500;
            // If IDE was already running, we need to wait for the workspace to reload
            int minWaitMs = wasAlreadyRunning ? 5000 : 3000;
            Process? ideProcess = null;

            while (waitedMs < maxWaitMs)
            {
                System.Threading.Thread.Sleep(intervalMs);
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
                        GD.Print($"[VSCodeAttacher] {ideName} ready after {waitedMs}ms (PID: {ideProcess.Id}, was running: {wasAlreadyRunning})");
                        break;
                    }
                }
            }

            if (ideProcess == null)
            {
                GD.PrintErr($"[VSCodeAttacher] {ideName} process not found after waiting");
                GD.Print($"[VSCodeAttacher] Please press F5 in {ideName} manually to start debugging.");
                return AttachResult.Ok();
            }

            // Step 3: Send F5 keypress to IDE using PowerShell
            GD.Print($"[VSCodeAttacher] Sending F5 keypress to {ideName}...");

            try
            {
                // Use AppActivate with process ID for reliable window activation
                var psCommand = $"Add-Type -AssemblyName Microsoft.VisualBasic; " +
                    $"[Microsoft.VisualBasic.Interaction]::AppActivate({ideProcess.Id}); " +
                    "Start-Sleep -Milliseconds 1000; " +
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "[System.Windows.Forms.SendKeys]::SendWait('{F5}')";

                var psProcess = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var ps = Process.Start(psProcess);
                ps?.WaitForExit(10000);

                GD.Print($"[VSCodeAttacher] F5 keypress sent to {ideName}.");
            }
            catch (Exception ex)
            {
                GD.Print($"[VSCodeAttacher] Could not send F5 keystroke: {ex.Message}");
                GD.Print($"[VSCodeAttacher] Please press F5 in {ideName} manually to start debugging.");
            }

            return AttachResult.Ok();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VSCodeAttacher] Exception: {ex.Message}");
            return AttachResult.Fail($"Exception: {ex.Message}");
        }
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
