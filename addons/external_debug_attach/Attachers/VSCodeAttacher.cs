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

            // Record current VS Code processes before launching
            var existingCodePids = Process.GetProcessesByName("Code")
                .Select(p => p.Id)
                .ToHashSet();

            // Step 1: Open VS Code with the workspace
            var openArgs = $"\"{workspacePath}\" --reuse-window";
            GD.Print($"[VSCodeAttacher] Opening workspace: \"{idePath}\" {openArgs}");

            var openProcess = new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = openArgs,
                UseShellExecute = true
            };
            Process.Start(openProcess);

            // Step 2: Wait for VS Code to be ready (new process or window ready)
            GD.Print("[VSCodeAttacher] Waiting for VS Code to be ready...");

            int waitedMs = 0;
            int maxWaitMs = 15000; // Max 15 seconds
            int intervalMs = 500;
            Process? codeProcess = null;

            while (waitedMs < maxWaitMs)
            {
                System.Threading.Thread.Sleep(intervalMs);
                waitedMs += intervalMs;

                // Check if there's a VS Code process running
                var codeProcesses = Process.GetProcessesByName("Code");
                if (codeProcesses.Length > 0)
                {
                    // Prefer a new process, otherwise use any existing one
                    codeProcess = codeProcesses
                        .FirstOrDefault(p => !existingCodePids.Contains(p.Id))
                        ?? codeProcesses.First();

                    // Wait a bit more for VS Code to fully load
                    if (waitedMs >= 3000)
                    {
                        GD.Print($"[VSCodeAttacher] VS Code ready after {waitedMs}ms (PID: {codeProcess.Id})");
                        break;
                    }
                }
            }

            if (codeProcess == null)
            {
                GD.PrintErr("[VSCodeAttacher] VS Code process not found after waiting");
                GD.Print("[VSCodeAttacher] Please press F5 in VS Code manually to start debugging.");
                return AttachResult.Ok();
            }

            // Step 3: Send F5 keypress to VS Code using PowerShell
            GD.Print("[VSCodeAttacher] Sending F5 keypress to start debugging...");

            try
            {
                // Use AppActivate with process ID for reliable window activation
                var psCommand = $"Add-Type -AssemblyName Microsoft.VisualBasic; " +
                    $"[Microsoft.VisualBasic.Interaction]::AppActivate({codeProcess.Id}); " +
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

                GD.Print("[VSCodeAttacher] F5 keypress sent to VS Code.");
            }
            catch (Exception ex)
            {
                GD.Print($"[VSCodeAttacher] Could not send F5 keystroke: {ex.Message}");
                GD.Print("[VSCodeAttacher] Please press F5 in VS Code manually to start debugging.");
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
