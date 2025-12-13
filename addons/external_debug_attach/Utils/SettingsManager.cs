using Godot;
using System.IO;
using SysEnv = System.Environment;

namespace ExternalDebugAttach;

/// <summary>
/// IDE type enumeration
/// </summary>
public enum IdeType
{
    Rider,
    VSCode
}

/// <summary>
/// Manages plugin settings using Godot EditorSettings
/// </summary>
public class SettingsManager
{
    private const string SettingPrefix = "external_debug_attach/";
    private const string SettingIdeType = SettingPrefix + "ide_type";
    private const string SettingIdePath = SettingPrefix + "ide_path";
    private const string SettingAttachDelayMs = SettingPrefix + "attach_delay_ms";
    private const string SettingSolutionPath = SettingPrefix + "solution_path";

    private EditorSettings _editorSettings;

    public SettingsManager()
    {
        _editorSettings = EditorInterface.Singleton.GetEditorSettings();
    }

    /// <summary>
    /// Initialize all plugin settings with default values
    /// </summary>
    public void InitializeSettings()
    {
        // IDE Type
        if (!_editorSettings.HasSetting(SettingIdeType))
        {
            _editorSettings.SetSetting(SettingIdeType, (int)IdeType.Rider);
        }
        AddSettingInfo(SettingIdeType, Variant.Type.Int, PropertyHint.Enum, "Rider,VSCode");

        // IDE Path
        if (!_editorSettings.HasSetting(SettingIdePath))
        {
            _editorSettings.SetSetting(SettingIdePath, "");
        }
        AddSettingInfo(SettingIdePath, Variant.Type.String, PropertyHint.GlobalFile, "*.exe");

        // Attach Delay
        if (!_editorSettings.HasSetting(SettingAttachDelayMs))
        {
            _editorSettings.SetSetting(SettingAttachDelayMs, 1000);
        }
        AddSettingInfo(SettingAttachDelayMs, Variant.Type.Int, PropertyHint.Range, "100,5000,100");

        // Solution Path
        if (!_editorSettings.HasSetting(SettingSolutionPath))
        {
            _editorSettings.SetSetting(SettingSolutionPath, DetectSolutionPath());
        }
        AddSettingInfo(SettingSolutionPath, Variant.Type.String, PropertyHint.GlobalFile, "*.sln");
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
        var value = _editorSettings.GetSetting(SettingIdeType);
        return (IdeType)(int)value;
    }

    /// <summary>
    /// Get the IDE executable path, auto-detect if empty
    /// </summary>
    public string GetIdePath()
    {
        var path = (string)_editorSettings.GetSetting(SettingIdePath);

        if (string.IsNullOrEmpty(path))
        {
            // Auto-detect based on IDE type
            var ideType = GetIdeType();
            path = ideType switch
            {
                IdeType.Rider => DetectRiderPath(),
                IdeType.VSCode => DetectVSCodePath(),
                _ => ""
            };
        }

        return path;
    }

    /// <summary>
    /// Get the attach delay in milliseconds
    /// </summary>
    public int GetAttachDelayMs()
    {
        return (int)_editorSettings.GetSetting(SettingAttachDelayMs);
    }

    /// <summary>
    /// Get the solution/workspace path
    /// </summary>
    public string GetSolutionPath()
    {
        var path = (string)_editorSettings.GetSetting(SettingSolutionPath);

        if (string.IsNullOrEmpty(path))
        {
            path = DetectSolutionPath();
        }

        return path;
    }

    /// <summary>
    /// Auto-detect .sln file in project directory
    /// </summary>
    private string DetectSolutionPath()
    {
        var projectPath = ProjectSettings.GlobalizePath("res://");
        var slnFiles = Directory.GetFiles(projectPath, "*.sln");

        if (slnFiles.Length > 0)
        {
            return slnFiles[0];
        }

        return "";
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
    /// Auto-detect VS Code installation path
    /// </summary>
    private string DetectVSCodePath()
    {
        // Common VS Code paths on Windows
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
