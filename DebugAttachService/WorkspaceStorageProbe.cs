using System.Text.Json;

namespace DebugAttachService;

/// <summary>
/// Detects when VS Code / Cursor has registered a folder workspace under
/// <c>%APPDATA%\[App]\User\workspaceStorage\&lt;id&gt;\workspace.json</c> — a disk signal independent of window title.
/// </summary>
internal static class WorkspaceStorageProbe
{
    public static bool IsWorkspaceFolderRegistered(string workspacePath, string processName)
    {
        var root = GetWorkspaceStorageRoot(processName);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return false;

        var targetFull = NormalizeLocalPath(workspacePath);
        if (string.IsNullOrEmpty(targetFull))
            return false;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var wj = Path.Combine(dir, "workspace.json");
            if (!File.Exists(wj))
                continue;

            try
            {
                var text = File.ReadAllText(wj);
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("folder", out var folderEl))
                    continue;
                var folderUri = folderEl.GetString();
                if (string.IsNullOrEmpty(folderUri))
                    continue;
                if (FolderUriMatchesLocalPath(folderUri, targetFull))
                    return true;
            }
            catch
            {
                // Partial JSON during write, or unexpected shape — keep polling.
            }
        }

        return false;
    }

    /// <summary>Same path used for <see cref="IsWorkspaceFolderRegistered"/>; may not exist yet on cold start.</summary>
    public static string? GetWorkspaceStorageRootPath(string processName)
    {
        var overridePath = Environment.GetEnvironmentVariable("DEBUG_ATTACH_WORKSPACE_STORAGE_ROOT")?.Trim();
        if (!string.IsNullOrEmpty(overridePath))
            return overridePath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return processName switch
        {
            "Cursor" => Path.Combine(appData, "Cursor", "User", "workspaceStorage"),
            "Code" => Path.Combine(appData, "Code", "User", "workspaceStorage"),
            "Antigravity" => Path.Combine(appData, "Antigravity", "User", "workspaceStorage"),
            _ => Path.Combine(appData, processName, "User", "workspaceStorage"),
        };
    }

    private static string? GetWorkspaceStorageRoot(string processName) => GetWorkspaceStorageRootPath(processName);

    private static string NormalizeLocalPath(string workspacePath)
    {
        try
        {
            return Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool FolderUriMatchesLocalPath(string folderUri, string targetFullNormalized)
    {
        if (!Uri.TryCreate(folderUri, UriKind.Absolute, out var uri))
            return false;
        if (!uri.IsFile)
            return false;

        var local = Uri.UnescapeDataString(uri.LocalPath);
        string normalized;
        try
        {
            normalized = Path.GetFullPath(local).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        return string.Equals(normalized, targetFullNormalized, StringComparison.OrdinalIgnoreCase);
    }
}
