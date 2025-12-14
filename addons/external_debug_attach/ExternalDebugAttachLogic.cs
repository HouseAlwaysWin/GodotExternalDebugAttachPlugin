using Godot;
using System;

namespace ExternalDebugAttach;

/// <summary>
/// C# logic for External Debug Attach functionality.
/// Called from GDScript wrapper to avoid delegate invalidation issues.
/// </summary>
public partial class ExternalDebugAttachLogic : RefCounted
{
    private SettingsManager? _settingsManager;

    /// <summary>
    /// Initialize the plugin logic
    /// </summary>
    public void Initialize()
    {
        GD.Print("[ExternalDebugAttach] C# logic initializing...");

        _settingsManager = new SettingsManager();
        _settingsManager.InitializeSettings();

        // Auto-register DebugWaitAutoload
        RegisterDebugWaitAutoload();

        GD.Print("[ExternalDebugAttach] C# logic initialized");
    }

    /// <summary>
    /// Cleanup when plugin is disabled
    /// </summary>
    public void Cleanup()
    {
        GD.Print("[ExternalDebugAttach] C# logic cleaning up...");

        UnregisterDebugWaitAutoload();

        GD.Print("[ExternalDebugAttach] C# logic cleaned up");
    }

    /// <summary>
    /// Main entry point - Run project and attach debugger
    /// </summary>
    public void RunAndAttach()
    {
        GD.Print("[ExternalDebugAttach] Run + Attach Debug triggered");

        try
        {
            if (_settingsManager == null)
            {
                _settingsManager = new SettingsManager();
                _settingsManager.InitializeSettings();
            }

            // Step 1: Get settings
            var ideType = _settingsManager.GetIdeType();
            var idePath = _settingsManager.GetIdePath();
            var attachDelayMs = _settingsManager.GetAttachDelayMs();
            var solutionPath = _settingsManager.GetSolutionPath();

            GD.Print($"[ExternalDebugAttach] IDE Type: {ideType}, Path: {idePath}");

            // Step 2: Run the project
            EditorInterface.Singleton.PlayMainScene();
            GD.Print("[ExternalDebugAttach] Project started");

            // Step 3-5: Run attach process in background
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(attachDelayMs);

                    var pid = ProcessScanner.FindGodotProcessPid();
                    if (pid == -1)
                    {
                        GD.PrintErr("[ExternalDebugAttach] Failed to find Godot process PID");
                        return;
                    }
                    GD.Print($"[ExternalDebugAttach] Found PID: {pid}");

                    IIdeAttacher attacher = ideType switch
                    {
                        IdeType.VSCode => new VSCodeAttacher(),
                        IdeType.Cursor => new VSCodeAttacher(),
                        IdeType.AntiGravity => new VSCodeAttacher(), // AntiGravity is VS Code-based
                        _ => throw new NotSupportedException($"IDE type {ideType} is not supported")
                    };

                    var result = attacher.Attach(pid, idePath, solutionPath);

                    if (result.Success)
                    {
                        GD.Print($"[ExternalDebugAttach] Successfully attached {ideType} to PID {pid}");
                    }
                    else
                    {
                        GD.PrintErr($"[ExternalDebugAttach] Failed to attach: {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ExternalDebugAttach] Background task error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExternalDebugAttach] Error: {ex.Message}");
        }
    }

    private const string AutoloadName = "DebugWait";
    private const string AutoloadPath = "res://addons/external_debug_attach/DebugWaitAutoload.cs";

    private void RegisterDebugWaitAutoload()
    {
        if (ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
        {
            GD.Print($"[ExternalDebugAttach] Autoload '{AutoloadName}' already registered");
            return;
        }

        ProjectSettings.SetSetting($"autoload/{AutoloadName}", AutoloadPath);
        ProjectSettings.Save();
        GD.Print($"[ExternalDebugAttach] Registered autoload '{AutoloadName}'");
    }

    private void UnregisterDebugWaitAutoload()
    {
        if (!ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
        {
            return;
        }

        ProjectSettings.SetSetting($"autoload/{AutoloadName}", new Variant());
        ProjectSettings.Save();
        GD.Print($"[ExternalDebugAttach] Unregistered autoload '{AutoloadName}'");
    }
}
