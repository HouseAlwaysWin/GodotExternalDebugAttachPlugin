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

        // Create and add toolbar button
        _attachButton = new Button();
        _attachButton.Text = "▶ Run + Attach Debug";
        _attachButton.TooltipText = "Run project and attach external debugger";
        _attachButton.Pressed += OnAttachButtonPressed;

        AddControlToContainer(CustomControlContainer.Toolbar, _attachButton);
    }

    public override void _ExitTree()
    {
        GD.Print("[ExternalDebugAttach] Plugin unloaded");

        // Clean up
        if (_attachButton != null)
        {
            RemoveControlFromContainer(CustomControlContainer.Toolbar, _attachButton);
            _attachButton.QueueFree();
            _attachButton = null;
        }
    }

    private async void OnAttachButtonPressed()
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

            // Step 1: Get settings
            var ideType = _settingsManager.GetIdeType();
            var idePath = _settingsManager.GetIdePath();
            var attachDelayMs = _settingsManager.GetAttachDelayMs();
            var solutionPath = _settingsManager.GetSolutionPath();

            GD.Print($"[ExternalDebugAttach] IDE Type: {ideType}, Path: {idePath}");

            // Step 2: Run the project
            EditorInterface.Singleton.PlayMainScene();
            GD.Print("[ExternalDebugAttach] Project started");

            // Step 3: Wait for attach delay
            await ToSignal(GetTree().CreateTimer(attachDelayMs / 1000.0), SceneTreeTimer.SignalName.Timeout);

            // Step 4: Find the Godot process PID
            var pid = ProcessScanner.FindGodotProcessPid();
            if (pid == -1)
            {
                ShowError("Failed to find Godot process PID");
                return;
            }
            GD.Print($"[ExternalDebugAttach] Found PID: {pid}");

            // Step 5: Attach debugger
            IIdeAttacher attacher = ideType switch
            {
                IdeType.Rider => new RiderAttacher(),
                IdeType.VSCode => new VSCodeAttacher(),
                _ => throw new NotSupportedException($"IDE type {ideType} is not supported")
            };

            var result = attacher.Attach(pid, idePath, solutionPath);

            if (result.Success)
            {
                ShowNotification($"Successfully attached {ideType} to PID {pid}");
            }
            else
            {
                ShowError($"Failed to attach: {result.ErrorMessage}");
            }
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
}
