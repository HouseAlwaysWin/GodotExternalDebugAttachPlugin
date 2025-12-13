using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using Godot;

namespace ExternalDebugAttach;

/// <summary>
/// Scans system processes to find the running Godot game process
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProcessScanner
{
    /// <summary>
    /// Find the PID of the running Godot game process
    /// </summary>
    /// <returns>Process ID, or -1 if not found</returns>
    public static int FindGodotProcessPid()
    {
        try
        {
            var projectPath = ProjectSettings.GlobalizePath("res://").Replace("/", "\\");
            GD.Print($"[ProcessScanner] Looking for Godot process with project path: {projectPath}");

            // Get all Godot processes
            var godotProcesses = Process.GetProcesses()
                .Where(p => IsGodotProcess(p.ProcessName))
                .ToList();

            GD.Print($"[ProcessScanner] Found {godotProcesses.Count} Godot processes");

            foreach (var process in godotProcesses)
            {
                try
                {
                    var commandLine = GetCommandLine(process.Id);
                    GD.Print($"[ProcessScanner] PID {process.Id}: {commandLine}");

                    // Skip the editor process (check if it's not using --editor flag)
                    if (commandLine.Contains("--editor") || commandLine.Contains("-e"))
                    {
                        continue;
                    }

                    // Check if this process is running our project
                    if (commandLine.Contains(projectPath) || IsRecentProcess(process))
                    {
                        GD.Print($"[ProcessScanner] Found matching process: PID {process.Id}");
                        return process.Id;
                    }
                }
                catch (Exception ex)
                {
                    GD.Print($"[ProcessScanner] Error checking process {process.Id}: {ex.Message}");
                }
            }

            // Fallback: return the most recent non-editor Godot process
            var recentProcess = godotProcesses
                .Where(p => !IsEditorProcess(p))
                .OrderByDescending(p => p.StartTime)
                .FirstOrDefault();

            if (recentProcess != null)
            {
                GD.Print($"[ProcessScanner] Using most recent Godot process: PID {recentProcess.Id}");
                return recentProcess.Id;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ProcessScanner] Error scanning processes: {ex.Message}");
        }

        return -1;
    }

    /// <summary>
    /// Check if a process name matches Godot executable patterns
    /// </summary>
    private static bool IsGodotProcess(string processName)
    {
        var name = processName.ToLowerInvariant();
        return name.Contains("godot") || name.Contains("godotsharp");
    }

    /// <summary>
    /// Check if this is the Godot editor process
    /// </summary>
    private static bool IsEditorProcess(Process process)
    {
        try
        {
            var commandLine = GetCommandLine(process.Id);
            return commandLine.Contains("--editor") || commandLine.Contains("-e");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if this process was started recently (within last 10 seconds)
    /// </summary>
    private static bool IsRecentProcess(Process process)
    {
        try
        {
            var age = DateTime.Now - process.StartTime;
            return age.TotalSeconds < 10;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the command line arguments for a process using WMI
    /// </summary>
    private static string GetCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");

            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString() ?? "";
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[ProcessScanner] WMI query failed for PID {processId}: {ex.Message}");
        }

        return "";
    }
}
