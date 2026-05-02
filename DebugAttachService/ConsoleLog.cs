namespace DebugAttachService;

/// <summary>
/// Colored stdout/stderr for interactive consoles. Disabled when output is redirected or <c>NO_COLOR</c> / <c>DEBUG_ATTACH_NO_COLOR</c> is set.
/// Force with <c>DEBUG_ATTACH_FORCE_COLOR=1</c> when redirected but terminal supports ANSI (rare on Windows CMD).
/// </summary>
public static class ConsoleLog
{
    private static bool UseColor =>
        (Environment.GetEnvironmentVariable("DEBUG_ATTACH_FORCE_COLOR") == "1"
         || !Console.IsOutputRedirected)
        && Environment.GetEnvironmentVariable("NO_COLOR") != "1"
        && Environment.GetEnvironmentVariable("DEBUG_ATTACH_NO_COLOR") != "1";

    public static void WriteLine() => Console.WriteLine();

    public static void WriteLine(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Console.WriteLine();
            return;
        }

        if (!UseColor)
        {
            Console.WriteLine(message);
            return;
        }

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ResolveColor(message);
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }

    public static void WriteErrorLine(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Console.Error.WriteLine();
            return;
        }

        if (!UseColor)
        {
            Console.Error.WriteLine(message);
            return;
        }

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = prev;
    }

    /// <summary>Startup banner (explicit colors).</summary>
    public static void WriteStartupBanner()
    {
        if (!UseColor)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  Debug Attach Service for Godot C#");
            Console.WriteLine("========================================");
            Console.WriteLine();
            return;
        }

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("========================================");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  Debug Attach Service for Godot C#");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("========================================");
        Console.ForegroundColor = prev;
        Console.WriteLine();
    }

    private static ConsoleColor ResolveColor(string m)
    {
        var t = m.TrimStart('\r', '\n');

        // Failures / errors in normal log lines
        if (t.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (t.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (t.Contains("process not found after waiting", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (t.Contains("Failed to auto-detect", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (t.Contains("Exception:", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (t.Contains("Error scanning processes", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;
        if (t.Contains("already in use", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Red;

        // Success
        if (t.Contains("OK —", StringComparison.Ordinal))
            return ConsoleColor.Green;
        if (t.Contains("listening on", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("Auto-detected game PID:", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("Auto-detected IDE path:", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("Found process with PID", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("Found recent Godot process:", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("Found recent dotnet process", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("Finished SendKeys", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (t.Contains("ready after", StringComparison.OrdinalIgnoreCase) && t.Contains("ms (PID:", StringComparison.Ordinal))
            return ConsoleColor.Green;

        // Experimental / retry / caution
        if (t.Contains("Experimental", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.DarkYellow;
        if (t.Contains("retrying", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.DarkYellow;
        if (t.Contains("Received empty request", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.DarkYellow;
        if (t.Contains("ignored (invalid format)", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Yellow;
        if (t.Contains("Please press", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Yellow;

        // Component tint
        if (t.StartsWith("[VSCodeAttacher]", StringComparison.Ordinal))
            return ConsoleColor.Cyan;

        if (t.StartsWith("[ProcessScanner]", StringComparison.Ordinal))
            return ConsoleColor.Magenta;

        if (t.StartsWith("[DebugAttachService]", StringComparison.Ordinal))
        {
            if (t.Contains("Received request:", StringComparison.Ordinal))
                return ConsoleColor.DarkCyan;
            if (t.Contains("Service stopped", StringComparison.Ordinal) || t.Contains("Shutting down", StringComparison.OrdinalIgnoreCase))
                return ConsoleColor.Yellow;
            return ConsoleColor.Gray;
        }

        // --help text
        if (t.StartsWith("Debug Attach Service - Godot", StringComparison.Ordinal))
            return ConsoleColor.White;
        if (t.StartsWith("Usage:", StringComparison.Ordinal))
            return ConsoleColor.DarkCyan;
        if (t == "Options:" || t == "Environment (optional):")
            return ConsoleColor.DarkYellow;
        if (t.StartsWith("The service listens", StringComparison.Ordinal))
            return ConsoleColor.DarkGray;

        return ConsoleColor.Gray;
    }
}
