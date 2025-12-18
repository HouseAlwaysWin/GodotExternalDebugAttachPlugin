using System.Diagnostics;

namespace DebugAttachService;

/// <summary>
/// Scans system processes to find the running Godot game process
/// </summary>
public static class ProcessScanner
{
    /// <summary>
    /// Find the PID of the running Godot game process
    /// </summary>
    /// <returns>Process ID, or -1 if not found</returns>
    public static int FindGodotProcessPid(Action<string>? log = null)
    {
        log ??= Console.WriteLine;

        try
        {
            var currentPid = Environment.ProcessId;
            log($"[ProcessScanner] Scanning for Godot game process (excluding self PID: {currentPid})");

            // Strategy 1: Look for Godot processes (the game might run as a Godot process)
            var godotProcesses = Process.GetProcesses()
                .Where(p => IsGodotProcess(p.ProcessName))
                .Where(p => p.Id != currentPid)
                .OrderByDescending(p => GetProcessStartTimeSafe(p))
                .ToList();

            log($"[ProcessScanner] Found {godotProcesses.Count} Godot processes");

            // If we found a Godot process that started recently (within last 15 seconds), use it
            var recentGodotProcess = godotProcesses
                .FirstOrDefault(p =>
                {
                    var startTime = GetProcessStartTimeSafe(p);
                    return startTime != DateTime.MinValue &&
                           (DateTime.Now - startTime).TotalSeconds < 15;
                });

            if (recentGodotProcess != null)
            {
                log($"[ProcessScanner] Found recent Godot process: PID {recentGodotProcess.Id}");
                return recentGodotProcess.Id;
            }

            // Strategy 2: For Godot 4.x with C#, the game runs via dotnet
            // Look for dotnet processes that started recently
            var dotnetProcesses = Process.GetProcessesByName("dotnet")
                .Where(p => p.Id != currentPid)
                .OrderByDescending(p => GetProcessStartTimeSafe(p))
                .ToList();

            log($"[ProcessScanner] Found {dotnetProcesses.Count} dotnet processes");

            // Find the most recent dotnet process (likely our game)
            var recentDotnetProcess = dotnetProcesses
                .FirstOrDefault(p =>
                {
                    var startTime = GetProcessStartTimeSafe(p);
                    if (startTime == DateTime.MinValue) return false;

                    var age = (DateTime.Now - startTime).TotalSeconds;
                    log($"[ProcessScanner] Checking dotnet PID {p.Id}, age: {age:F1}s");

                    // Consider processes started within last 20 seconds
                    return age < 20;
                });

            if (recentDotnetProcess != null)
            {
                log($"[ProcessScanner] Found recent dotnet process (likely game): PID {recentDotnetProcess.Id}");
                return recentDotnetProcess.Id;
            }

            // Strategy 3: If we still have Godot processes, use the newest one
            if (godotProcesses.Count > 0)
            {
                var bestMatch = godotProcesses[0];
                log($"[ProcessScanner] Using most recent Godot process: PID {bestMatch.Id}");
                return bestMatch.Id;
            }

            log("[ProcessScanner] No suitable process found");
        }
        catch (Exception ex)
        {
            log($"[ProcessScanner] Error scanning processes: {ex.Message}");
        }

        return -1;
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
        return name.Contains("godot");
    }
}
