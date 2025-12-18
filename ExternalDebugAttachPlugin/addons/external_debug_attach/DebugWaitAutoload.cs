using Godot;
using System;
using System.Diagnostics;

namespace ExternalDebugAttach;

/// <summary>
/// Autoload script that pauses the game until the debugger is attached.
/// This ensures no breakpoints are missed during game startup.
/// </summary>
public partial class DebugWaitAutoload : Node
{
    /// <summary>
    /// Maximum time to wait for debugger (in seconds)
    /// </summary>
    [Export]
    public float MaxWaitSeconds { get; set; } = 30.0f;

    private bool _waitingForDebugger = false;
    private double _waitStartTime = 0;
    private double _originalTimeScale = 1.0;
    private Label? _waitLabel;

    public override void _Ready()
    {
        // Only wait if running from editor (debug mode)
        if (!OS.IsDebugBuild())
        {
            GD.Print("[DebugWait] Release build - skipping debugger wait");
            return;
        }

        // Check if debugger is already attached
        if (Debugger.IsAttached)
        {
            GD.Print("[DebugWait] Debugger already attached");
            return;
        }

        GD.Print("[DebugWait] Waiting for debugger to attach...");
        GD.Print("[DebugWait] (Press ESC in game window to skip, or wait for timeout)");

        // Show a visual indicator
        CreateWaitOverlay();

        // SYNCHRONOUS BLOCKING WAIT - this prevents other _Ready from running
        var startTime = DateTime.Now;
        var timeout = TimeSpan.FromSeconds(MaxWaitSeconds);

        while (!Debugger.IsAttached)
        {
            // Check timeout
            if (DateTime.Now - startTime > timeout)
            {
                GD.PrintErr($"[DebugWait] Timeout after {MaxWaitSeconds}s - resuming without debugger");
                break;
            }

            // Small delay to prevent CPU spinning
            System.Threading.Thread.Sleep(100);

            // Update label (we need to process events for display)
            if (_waitLabel != null)
            {
                var remaining = MaxWaitSeconds - (DateTime.Now - startTime).TotalSeconds;
                _waitLabel.Text = $"Waiting for debugger... ({remaining:F1}s)\nPress ESC to skip";
            }
        }

        if (Debugger.IsAttached)
        {
            GD.Print("[DebugWait] Debugger attached! Resuming game...");
        }

        // Cleanup overlay
        if (_waitLabel != null)
        {
            _waitLabel.GetParent()?.QueueFree();
        }

        GD.Print("[DebugWait] Game resumed");
    }

    public override void _Process(double delta)
    {
        if (!_waitingForDebugger)
            return;

        // Check if debugger is now attached
        if (Debugger.IsAttached)
        {
            GD.Print("[DebugWait] Debugger attached! Resuming game...");
            ResumeGame();
            return;
        }

        // Check for timeout
        var elapsed = Time.GetUnixTimeFromSystem() - _waitStartTime;
        if (elapsed >= MaxWaitSeconds)
        {
            GD.PrintErr($"[DebugWait] Timeout after {MaxWaitSeconds}s - resuming without debugger");
            ResumeGame();
            return;
        }

        // Update the wait label
        if (_waitLabel != null)
        {
            var remaining = MaxWaitSeconds - elapsed;
            _waitLabel.Text = $"Waiting for debugger... ({remaining:F1}s)\nPress ESC to skip";
        }

        // Allow user to skip by pressing ESC
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            GD.Print("[DebugWait] User skipped debugger wait");
            ResumeGame();
        }
    }

    private void CreateWaitOverlay()
    {
        // Create a simple overlay to indicate waiting
        var overlay = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.7f),
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };

        _waitLabel = new Label
        {
            Text = "Waiting for debugger...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        _waitLabel.AddThemeColorOverride("font_color", Colors.White);
        _waitLabel.AddThemeFontSizeOverride("font_size", 24);

        var canvasLayer = new CanvasLayer { Layer = 100 };
        canvasLayer.AddChild(overlay);
        canvasLayer.AddChild(_waitLabel);
        AddChild(canvasLayer);
    }

    private void ResumeGame()
    {
        _waitingForDebugger = false;

        // Remove overlay
        foreach (var child in GetChildren())
        {
            if (child is CanvasLayer)
            {
                child.QueueFree();
            }
        }

        // Resume the scene tree and restore time scale
        GetTree().Paused = false;
        Engine.TimeScale = _originalTimeScale;

        GD.Print("[DebugWait] Game resumed");
    }
}
