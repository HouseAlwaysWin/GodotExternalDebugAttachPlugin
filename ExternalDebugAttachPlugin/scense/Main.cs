using Godot;
using System;

public partial class Main : Node2D
{
    private int _counter = 0;
    private Label? _label;
    private Timer? _timer;

    public override void _Ready()
    {
        GD.Print("Main scene ready - try setting a breakpoint here!");

        // Create a label to display counter
        _label = new Label
        {
            Text = "Counter: 0",
            Position = new Vector2(50, 50),
            Theme = new Theme()
        };
        _label.AddThemeFontSizeOverride("font_size", 32);
        AddChild(_label);

        // Create a timer that ticks every second
        _timer = new Timer
        {
            WaitTime = 1.0,
            Autostart = true
        };
        _timer.Timeout += OnTimerTimeout;
        AddChild(_timer);

        // Test method call - good breakpoint location
        TestDebuggerAttach();
    }

    private void TestDebuggerAttach()
    {
        // Set a breakpoint on the next line to test debugger!
        var message = "Debugger test: If you see this in console, try setting a breakpoint!";
        GD.Print(message);

        // Some test data for debugging
        var testData = new System.Collections.Generic.Dictionary<string, object>
        {
            { "player_name", "TestPlayer" },
            { "score", 100 },
            { "level", 1 },
            { "health", 100.0f }
        };

        foreach (var kvp in testData)
        {
            GD.Print($"  {kvp.Key}: {kvp.Value}");
        }
    }

    private void OnTimerTimeout()
    {
        // Good location for breakpoint - called repeatedly
        _counter++;

        if (_label != null)
        {
            _label.Text = $"Counter: {_counter}";
        }

        // Log every 5 seconds
        if (_counter % 5 == 0)
        {
            GD.Print($"Timer tick: {_counter} (breakpoint here to debug timer)");
        }
    }

    public override void _Process(double delta)
    {
        // Press Space to trigger a debug event
        if (Input.IsActionJustPressed("ui_accept"))
        {
            GD.Print("Space pressed - debug event triggered!");
            OnSpacePressed();
        }

        // Press Escape to quit
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            GD.Print("Escape pressed - quitting...");
            GetTree().Quit();
        }
    }

    private void OnSpacePressed()
    {
        // Good breakpoint location for input testing
        var currentTime = DateTime.Now;
        var randomValue = new Random().Next(1, 100);

        GD.Print($"  Time: {currentTime:HH:mm:ss}");
        GD.Print($"  Random: {randomValue}");
        GD.Print($"  Counter: {_counter}");
    }
}
