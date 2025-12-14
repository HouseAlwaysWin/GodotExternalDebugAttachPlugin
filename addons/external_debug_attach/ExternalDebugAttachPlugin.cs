using Godot;
using System;

namespace ExternalDebugAttach;

/// <summary>
/// Main EditorPlugin for External Debug Attach functionality.
/// Provides a one-click button to run the project and attach an external IDE debugger.
/// </summary>
[Tool]
public partial class ExternalDebugAttachPlugin : EditorPlugin
{
    private Button? _attachButton;
    private SettingsManager? _settingsManager;

    public override void _EnterTree()
    {
        GD.Print("[ExternalDebugAttach] Plugin loaded");

        // Initialize settings
        _settingsManager = new SettingsManager();
        _settingsManager.InitializeSettings();

        // Create and add toolbar button with icon
        _attachButton = new Button();
        _attachButton.TooltipText = "Run + Attach Debug (Alt+F5)";
        _attachButton.Pressed += OnAttachButtonPressed;

        // Load and set button icon (SVG for better quality)
        var iconPath = "res://addons/external_debug_attach/attach_icon.svg";
        var icon = GD.Load<Texture2D>(iconPath);
        if (icon != null)
        {
            _attachButton.Icon = icon;
        }
        else
        {
            // Fallback to text if icon not found
            _attachButton.Text = "▶ Attach";
        }

        AddControlToContainer(CustomControlContainer.Toolbar, _attachButton);

        // Auto-register DebugWaitAutoload
        RegisterDebugWaitAutoload();

        // Register keyboard shortcut (Ctrl+Alt+D)
        RegisterShortcut();
    }

    private void RegisterShortcut()
    {
        var shortcut = new Shortcut();
        var inputEvent = new InputEventKey();
        inputEvent.Keycode = Key.F5;
        inputEvent.AltPressed = true;
        shortcut.Events = new Godot.Collections.Array { inputEvent };

        _attachButton!.Shortcut = shortcut;
        _attachButton.ShortcutInTooltip = true;
        GD.Print("[ExternalDebugAttach] Registered shortcut: Alt+F5");
    }

    public override void _ExitTree()
    {
        GD.Print("[ExternalDebugAttach] Plugin unloading...");

        // Clean up - unsubscribe event FIRST to prevent delegate errors
        if (_attachButton != null)
        {
            try
            {
                _attachButton.Pressed -= OnAttachButtonPressed;
            }
            catch
            {
                // Delegate error detected - show restart reminder
                ShowRestartReminder();
            }

            RemoveControlFromContainer(CustomControlContainer.Toolbar, _attachButton);
            _attachButton.QueueFree();
            _attachButton = null;
        }

        // Unregister autoload when plugin is disabled
        UnregisterDebugWaitAutoload();

        GD.Print("[ExternalDebugAttach] Plugin unloaded");
    }

    private void ShowRestartReminder()
    {
        var dialog = new AcceptDialog();
        dialog.Title = "External Debug Attach";
        dialog.DialogText = "偵測到 .NET 程式集重載錯誤。\n\n請重新啟動 Godot 編輯器以確保 plugin 正常運作。\n\n(This is a known Godot bug #78513)";
        dialog.OkButtonText = "OK";
        EditorInterface.Singleton.GetBaseControl().AddChild(dialog);
        dialog.PopupCentered();
    }

    private void OnAttachButtonPressed()
    {
        GD.Print("[ExternalDebugAttach] Run + Attach Debug triggered");

        try
        {
            // Check if settings manager is initialized
            if (_settingsManager == null)
            {
                ShowError("Plugin not properly initialized");
                return;
            }

            // Step 1: Get settings (capture values before any async operations)
            var ideType = _settingsManager.GetIdeType();
            var idePath = _settingsManager.GetIdePath();
            var attachDelayMs = _settingsManager.GetAttachDelayMs();
            var solutionPath = _settingsManager.GetSolutionPath();

            GD.Print($"[ExternalDebugAttach] IDE Type: {ideType}, Path: {idePath}");

            // Step 2: Run the project
            EditorInterface.Singleton.PlayMainScene();
            GD.Print("[ExternalDebugAttach] Project started");

            // Step 3-5: Run attach process in background thread to avoid blocking
            // and to prevent delegate invalidation issues
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Wait for attach delay
                    System.Threading.Thread.Sleep(attachDelayMs);

                    // Find the Godot process PID
                    var pid = ProcessScanner.FindGodotProcessPid();
                    if (pid == -1)
                    {
                        GD.PrintErr("[ExternalDebugAttach] Failed to find Godot process PID");
                        return;
                    }
                    GD.Print($"[ExternalDebugAttach] Found PID: {pid}");

                    // Attach debugger
                    IIdeAttacher attacher = ideType switch
                    {
                        // IdeType.Rider => new RiderAttacher(),
                        IdeType.VSCode => new VSCodeAttacher(),
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
            ShowError($"Error: {ex.Message}");
        }
    }

    private void ShowNotification(string message)
    {
        GD.Print($"[ExternalDebugAttach] {message}");
        // TODO: Show toast notification in editor
    }

    private void ShowError(string message)
    {
        GD.PrintErr($"[ExternalDebugAttach] {message}");
        // TODO: Show error dialog in editor
    }

    private const string AutoloadName = "DebugWait";
    private const string AutoloadPath = "res://addons/external_debug_attach/DebugWaitAutoload.cs";

    private void RegisterDebugWaitAutoload()
    {
        // Check if already registered
        if (ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
        {
            GD.Print($"[ExternalDebugAttach] Autoload '{AutoloadName}' already registered");
            return;
        }

        // Register the autoload
        ProjectSettings.SetSetting($"autoload/{AutoloadName}", AutoloadPath);
        ProjectSettings.Save();
        GD.Print($"[ExternalDebugAttach] Registered autoload '{AutoloadName}'");
    }

    private void UnregisterDebugWaitAutoload()
    {
        // Check if registered
        if (!ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
        {
            return;
        }

        // Remove the autoload
        ProjectSettings.SetSetting($"autoload/{AutoloadName}", new Variant());
        ProjectSettings.Save();
        GD.Print($"[ExternalDebugAttach] Unregistered autoload '{AutoloadName}'");
    }
}
