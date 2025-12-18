namespace DebugAttachService;

/// <summary>
/// Auto-detect IDE installation paths
/// </summary>
public static class IdePathDetector
{
    /// <summary>
    /// Detect IDE path based on editor type
    /// </summary>
    public static string DetectIdePath(string editor)
    {
        return editor.ToLowerInvariant() switch
        {
            "cursor" => DetectCursorPath(),
            "antigravity" => DetectAntiGravityPath(),
            _ => DetectVSCodePath()
        };
    }

    private static string DetectVSCodePath()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var programFiles = Environment.GetEnvironmentVariable("PROGRAMFILES") ?? "";
        var programFilesX86 = Environment.GetEnvironmentVariable("PROGRAMFILES(X86)") ?? "";

        string[] possiblePaths =
        {
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var codeCmdPath = Path.Combine(dir.Trim(), "code.cmd");
                if (File.Exists(codeCmdPath))
                {
                    // Navigate from bin to install dir
                    var binDir = Path.GetDirectoryName(codeCmdPath);
                    var installDir = binDir != null ? Directory.GetParent(binDir)?.FullName : null;
                    if (installDir != null)
                    {
                        var exePath = Path.Combine(installDir, "Code.exe");
                        if (File.Exists(exePath))
                        {
                            return exePath;
                        }
                    }
                }
            }
            catch { }
        }

        return "";
    }

    private static string DetectCursorPath()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var programFiles = Environment.GetEnvironmentVariable("PROGRAMFILES") ?? "";

        string[] possiblePaths =
        {
            Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe"),
            Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe"),
            Path.Combine(programFiles, "Cursor", "Cursor.exe"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Check PATH for cursor.cmd
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var cursorCmdPath = Path.Combine(dir.Trim(), "cursor.cmd");
                if (File.Exists(cursorCmdPath))
                {
                    // Navigate up to find Cursor.exe
                    var currentDir = Path.GetDirectoryName(cursorCmdPath);
                    for (int i = 0; i < 4 && !string.IsNullOrEmpty(currentDir); i++)
                    {
                        var exePath = Path.Combine(currentDir, "Cursor.exe");
                        if (File.Exists(exePath))
                        {
                            return exePath;
                        }
                        currentDir = Directory.GetParent(currentDir)?.FullName;
                    }
                }
            }
            catch { }
        }

        return "";
    }

    private static string DetectAntiGravityPath()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var programFiles = Environment.GetEnvironmentVariable("PROGRAMFILES") ?? "";

        string[] possiblePaths =
        {
            Path.Combine(localAppData, "Programs", "AntiGravity", "Antigravity.exe"),
            Path.Combine(localAppData, "Programs", "antigravity", "Antigravity.exe"),
            Path.Combine(programFiles, "AntiGravity", "Antigravity.exe"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "";
    }
}
