using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using SysEnv = System.Environment;

namespace ExternalDebugAttach;

/// <summary>
/// IDE type enumeration
/// </summary>
public enum IdeType
{
    VSCode,
    Cursor,
    AntiGravity
}

/// <summary>
/// Manages plugin settings using Godot EditorSettings
/// </summary>
public class SettingsManager
{
    private const string SettingPrefix = "external_debug_attach/";
    private const string SettingIdeType = SettingPrefix + "ide_type";
    private const string SettingVSCodePath = SettingPrefix + "vscode_path";
    private const string SettingCursorPath = SettingPrefix + "cursor_path";
    private const string SettingAntiGravityPath = SettingPrefix + "antigravity_path";


    // Deprecated settings (for cleanup)
    private const string SettingIdePath = SettingPrefix + "ide_path";
    private const string SettingSolutionPath = SettingPrefix + "solution_path";

    private EditorSettings _editorSettings;
    private IdeType _previousIdeType;

    public SettingsManager()
    {
        _editorSettings = EditorInterface.Singleton.GetEditorSettings();
    }

    /// <summary>
    /// Initialize all plugin settings with default values
    /// </summary>
    public void InitializeSettings()
    {
        // Cleanup deprecated settings
        if (_editorSettings.HasSetting(SettingIdePath)) _editorSettings.Erase(SettingIdePath);
        if (_editorSettings.HasSetting(SettingSolutionPath)) _editorSettings.Erase(SettingSolutionPath);

        // IDE Type dropdown
        if (!_editorSettings.HasSetting(SettingIdeType))
        {
            _editorSettings.SetSetting(SettingIdeType, (int)IdeType.VSCode);
        }
        AddSettingInfo(SettingIdeType, Variant.Type.Int, PropertyHint.Enum, "VSCode,Cursor,AntiGravity");

        // VS Code Path
        if (!_editorSettings.HasSetting(SettingVSCodePath))
        {
            _editorSettings.SetSetting(SettingVSCodePath, "");
        }
        AddSettingInfo(SettingVSCodePath, Variant.Type.String, PropertyHint.GlobalFile, "*.exe");

        // Cursor Path
        if (!_editorSettings.HasSetting(SettingCursorPath))
        {
            _editorSettings.SetSetting(SettingCursorPath, "");
        }
        AddSettingInfo(SettingCursorPath, Variant.Type.String, PropertyHint.GlobalFile, "*.exe");

        // AntiGravity Path
        if (!_editorSettings.HasSetting(SettingAntiGravityPath))
        {
            _editorSettings.SetSetting(SettingAntiGravityPath, "");
        }
        AddSettingInfo(SettingAntiGravityPath, Variant.Type.String, PropertyHint.GlobalFile, "*.exe");



        // Store the initial IDE type for change detection
        _previousIdeType = GetIdeType();

        // Connect to settings changed signal
        _editorSettings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// Cleanup event handlers
    /// </summary>
    public void Cleanup()
    {
        _editorSettings.SettingsChanged -= OnSettingsChanged;
    }

    /// <summary>
    /// Called when any editor setting changes
    /// </summary>
    private void OnSettingsChanged()
    {
        var currentIdeType = GetIdeType();
        if (currentIdeType != _previousIdeType)
        {
            GD.Print($"[ExternalDebugAttach] IDE Type changed from {_previousIdeType} to {currentIdeType}, triggering build...");
            _previousIdeType = currentIdeType;
            TriggerBuild();
        }
    }

    /// <summary>
    /// Trigger Godot's C# project build
    /// </summary>
    private void TriggerBuild()
    {
        try
        {
            var projectPath = ProjectSettings.GlobalizePath("res://");
            GD.Print($"[ExternalDebugAttach] Running dotnet build in: {projectPath}");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build",
                WorkingDirectory = projectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var process = System.Diagnostics.Process.Start(startInfo);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            GD.Print($"[ExternalDebugAttach] Build completed successfully");
                        }
                        else
                        {
                            GD.PrintErr($"[ExternalDebugAttach] Build failed with code {process.ExitCode}");
                            if (!string.IsNullOrEmpty(error))
                            {
                                GD.PrintErr($"[ExternalDebugAttach] Build error: {error}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ExternalDebugAttach] Build process error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExternalDebugAttach] Failed to trigger build: {ex.Message}");
        }
    }

    private void AddSettingInfo(string name, Variant.Type type, PropertyHint hint, string hintString)
    {
        var info = new Godot.Collections.Dictionary
        {
            { "name", name },
            { "type", (int)type },
            { "hint", (int)hint },
            { "hint_string", hintString }
        };
        _editorSettings.AddPropertyInfo(info);
    }

    /// <summary>
    /// Get the selected IDE type
    /// </summary>
    public IdeType GetIdeType()
    {
        return (IdeType)(int)_editorSettings.GetSetting(SettingIdeType);
    }

    /// <summary>
    /// Get the IDE executable path, auto-detect if empty
    /// </summary>
    public string GetIdePath()
    {
        var ideType = GetIdeType();

        if (ideType == IdeType.Cursor)
        {
            var path = (string)_editorSettings.GetSetting(SettingCursorPath);
            return string.IsNullOrEmpty(path) ? DetectCursorPath() : path;
        }
        else if (ideType == IdeType.AntiGravity)
        {
            var path = (string)_editorSettings.GetSetting(SettingAntiGravityPath);
            return string.IsNullOrEmpty(path) ? DetectAntiGravityPath() : path;
        }
        else // VSCode
        {
            var path = (string)_editorSettings.GetSetting(SettingVSCodePath);
            return string.IsNullOrEmpty(path) ? DetectVSCodePath() : path;
        }
    }



    /// <summary>
    /// Get the solution/workspace path
    /// </summary>
    public string GetSolutionPath()
    {
        // Always auto-detect .sln file
        return DetectSolutionPath();
    }

    /// <summary>
    /// Auto-detect .sln file in project directory, or create one if .csproj exists
    /// </summary>
    private string DetectSolutionPath()
    {
        var projectPath = ProjectSettings.GlobalizePath("res://");
        var slnFiles = Directory.GetFiles(projectPath, "*.sln");

        if (slnFiles.Length > 0)
        {
            return slnFiles[0];
        }

        // If no .sln found, look for .csproj
        var csprojFiles = Directory.GetFiles(projectPath, "*.csproj");
        if (csprojFiles.Length > 0)
        {
            var csprojPath = csprojFiles[0];
            var projectName = Path.GetFileNameWithoutExtension(csprojPath);

            GD.Print($"[ExternalDebugAttach] No solution found. Generating '{projectName}.sln'...");

            try
            {
                // Create new solution
                RunDotnetCommand("new sln -n \"" + projectName + "\"", projectPath);

                // Add project to solution
                RunDotnetCommand("sln add \"" + Path.GetFileName(csprojPath) + "\"", projectPath);

                var newSlnPath = Path.Combine(projectPath, projectName + ".sln");
                if (File.Exists(newSlnPath))
                {
                    GD.Print($"[ExternalDebugAttach] Generated solution: {newSlnPath}");
                    return newSlnPath;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ExternalDebugAttach] Failed to generate solution: {ex.Message}");
            }
        }

        return "";
    }

    private void RunDotnetCommand(string arguments, string workingDirectory)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo);
        process?.WaitForExit();
    }

    /// <summary>
    /// Auto-detect Rider installation path
    /// </summary>
    private string DetectRiderPath()
    {
        // Common Rider paths on Windows
        string[] possiblePaths =
        {
            Path.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.LocalApplicationData),
                "JetBrains", "Toolbox", "apps", "Rider", "ch-0"),
            Path.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFiles),
                "JetBrains"),
            Path.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFilesX86),
                "JetBrains")
        };

        foreach (var basePath in possiblePaths)
        {
            if (!Directory.Exists(basePath)) continue;

            // Search for rider64.exe
            try
            {
                var riderExes = Directory.GetFiles(basePath, "rider64.exe", SearchOption.AllDirectories);
                if (riderExes.Length > 0)
                {
                    return riderExes[0];
                }
            }
            catch
            {
                // Ignore access denied errors
            }
        }

        return "";
    }

    /// <summary>
    /// Auto-detect Cursor installation path
    /// </summary>
    private string DetectCursorPath()
    {
        // 1. Check PATH environment variable for "cursor.cmd" or "cursor"
        var pathEnv = SysEnv.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        foreach (var p in paths)
        {
            try
            {
                // Check for cursor.cmd in PATH
                var cursorCmd = Path.Combine(p.Trim(), "cursor.cmd");
                if (File.Exists(cursorCmd))
                {
                    // New Cursor structure (0.45+):
                    // .../cursor/Cursor.exe
                    // .../cursor/resources/app/bin/cursor.cmd
                    // Navigate up from bin -> app -> resources -> cursor
                    var currentDir = Path.GetDirectoryName(cursorCmd);
                    for (int i = 0; i < 4 && !string.IsNullOrEmpty(currentDir); i++)
                    {
                        var exePath = Path.Combine(currentDir, "Cursor.exe");
                        if (File.Exists(exePath)) return exePath;
                        currentDir = Directory.GetParent(currentDir)?.FullName;
                    }
                }

                // Also check for Cursor.exe directly in PATH
                var directExe = Path.Combine(p.Trim(), "Cursor.exe");
                if (File.Exists(directExe)) return directExe;
            }
            catch { }
        }

        // 2. Common Cursor paths on Windows (check multiple drives)
        var localAppData = SysEnv.GetFolderPath(SysEnv.SpecialFolder.LocalApplicationData);
        var programFiles = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFiles);
        var programFilesX86 = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFilesX86);

        // Build paths for both C: and D: drives (common secondary user data location)
        var basePaths = new List<string>
        {
            Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe"),
            Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe"),
            Path.Combine(programFiles, "Cursor", "Cursor.exe"),
            Path.Combine(programFilesX86, "Cursor", "Cursor.exe"),
        };

        // Also check D: drive equivalent paths
        if (localAppData.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
        {
            basePaths.Add("D:" + localAppData.Substring(2).Replace("AppData\\Local", "AppData\\Local\\Programs\\cursor\\Cursor.exe"));
            basePaths.Add(localAppData.Replace("C:", "D:").Replace("Local", "Local\\Programs\\cursor\\Cursor.exe"));
        }

        foreach (var path in basePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "";
    }

    /// <summary>
    /// Auto-detect AntiGravity installation path
    /// </summary>
    private string DetectAntiGravityPath()
    {
        // AntiGravity is VS Code-based, check common installation paths
        var localAppData = SysEnv.GetFolderPath(SysEnv.SpecialFolder.LocalApplicationData);
        var programFiles = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFiles);
        var programFilesX86 = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFilesX86);

        // Check PATH environment for antigravity.cmd or Antigravity.exe
        var pathEnv = SysEnv.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        foreach (var p in paths)
        {
            try
            {
                var cmd = Path.Combine(p.Trim(), "antigravity.cmd");
                if (File.Exists(cmd))
                {
                    var currentDir = Path.GetDirectoryName(cmd);
                    for (int i = 0; i < 4 && !string.IsNullOrEmpty(currentDir); i++)
                    {
                        var exePath = Path.Combine(currentDir, "Antigravity.exe");
                        if (File.Exists(exePath)) return exePath;
                        currentDir = Directory.GetParent(currentDir)?.FullName;
                    }
                }

                var directExe = Path.Combine(p.Trim(), "Antigravity.exe");
                if (File.Exists(directExe)) return directExe;
            }
            catch { }
        }

        // Common AntiGravity paths on Windows
        string[] possiblePaths =
        {
            Path.Combine(localAppData, "Programs", "AntiGravity", "Antigravity.exe"),
            Path.Combine(localAppData, "Programs", "antigravity", "Antigravity.exe"),
            Path.Combine(programFiles, "AntiGravity", "Antigravity.exe"),
            Path.Combine(programFilesX86, "AntiGravity", "Antigravity.exe"),
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

    /// <summary>
    /// Auto-detect VS Code installation path
    /// </summary>
    private string DetectVSCodePath()
    {
        // 1. Check PATH environment variable for "code.cmd" or "code.exe"
        var pathEnv = SysEnv.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        foreach (var p in paths)
        {
            try
            {
                var fullPath = Path.Combine(p.Trim(), "code.cmd");
                if (File.Exists(fullPath))
                {
                    // code.cmd usually points to bin folder, we need the exe in the parent/root usually, 
                    // or we can use the exe if found in the same folder. 
                    // However, users often have "Code.exe" in the main installation folder.
                    // Let's look for "Code.exe" in the parent folder of "bin" if code.cmd is in "bin"

                    // Standard VS Code structure:
                    // .../Microsoft VS Code/Code.exe
                    // .../Microsoft VS Code/bin/code.cmd

                    var binDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(binDir))
                    {
                        var installDir = Directory.GetParent(binDir)?.FullName;

                        if (installDir != null)
                        {
                            var exePath = Path.Combine(installDir, "Code.exe");
                            if (File.Exists(exePath)) return exePath;
                        }
                    }
                }

                // Also check for Code.exe directly in PATH (less common but possible)
                var directExe = Path.Combine(p.Trim(), "Code.exe");
                if (File.Exists(directExe)) return directExe;
            }
            catch { }
        }

        // 2. Common VS Code paths on Windows
        string[] possiblePaths =
        {
            Path.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFiles),
                "Microsoft VS Code", "Code.exe"),
            Path.Combine(SysEnv.GetFolderPath(SysEnv.SpecialFolder.ProgramFilesX86),
                "Microsoft VS Code", "Code.exe")
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
