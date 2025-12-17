using System;
using System.Diagnostics;
using System.Linq;
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

            // Simplified detection logic without WMI:
            // 1. Filter out editor processes by checking WindowTitle (Editor usually has specific title format)
            // 2. Prefer processes that are not the editor
            // 3. Select the most recently started process

            // Filter out known editor processes based on WindowTitle
            // Editor title usually starts with "project_name - Godot Engine" or similar, 
            // but game window title is usually just "project_name" or "debug".
            // However, this is flaky.
            // Better heuristic: The game process is usually started AFTER the editor.
            // And we can try to exclude the current process (the editor running this plugin).

            var currentPid = System.Environment.ProcessId;

            // Sort by start time descending (newest first)
            var candidates = godotProcesses
                .Where(p => p.Id != currentPid) // Exclude self (editor)
                .OrderByDescending(p => GetProcessStartTimeSafe(p))
                .ToList();

            if (candidates.Count > 0)
            {
                // First candidate is likely the game (since it was just started)
                var bestMatch = candidates[0];
                GD.Print($"[ProcessScanner] Using most recent Godot process (excluding self): PID {bestMatch.Id} Name: {bestMatch.ProcessName} Title: {bestMatch.MainWindowTitle}");
                return bestMatch.Id;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ProcessScanner] Error scanning processes: {ex.Message}");
        }

        return -1;
    }

    /// <summary>
    /// Check if command line indicates this is the editor process
    /// </summary>
    private static bool IsEditorCommandLine(string commandLine)
    {
        // Check for explicit --editor flag
        if (commandLine.Contains(" --editor") || commandLine.Contains("--editor "))
        {
            return true;
        }
        // Check for -e flag with proper boundaries (not part of path)
        // Look for " -e " or " -e\n" or end with " -e"
        if (commandLine.Contains(" -e ") || commandLine.EndsWith(" -e") || commandLine.Contains(" -e\t"))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get process start time safely
    /// </summary>
    private static DateTime GetProcessStartTimeSafe(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Check if a process name matches Godot executable patterns
    /// </summary>
    private static bool IsGodotProcess(string processName)
    {
        var name = processName.ToLowerInvariant();
        return name.Contains("godot") || name.Contains("godotsharp");
    }

}
