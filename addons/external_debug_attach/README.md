# External Debug Attach Plugin

**English** | [中文](README_zh_CN.md)

One-click Run + Attach Debug to external IDE (VS Code) for Godot Editor.

## Features

- 🚀 One-click to run game and attach debugger
- 🔧 Supports Visual Studio Code
- ⏳ Optional wait for debugger (never miss initialization breakpoints using Autoload)
- 🎯 Auto-detect IDE and solution paths
- ⌨️ Keyboard shortcut support: **Alt+F5**

## Installation

1. Copy the `addons/external_debug_attach/` folder to your Godot project.
2. Rebuild your C# project (ensure the plugin compiles successfully).
3. In Godot Editor: Go to **Project** → **Project Settings** → **Plugins**.
4. Enable the **"External Debug Attach"** plugin.

## Configuration

Go to **Editor** → **Editor Settings** and find the "External Debug Attach" section:

| Setting | Description |
|---------|-------------|
| IDE Type | Choose your IDE: `VSCode` |
| IDE Path | Path to the IDE executable (leave empty to auto-detect) |
| Attach Delay Ms | Delay before attaching (in milliseconds) |
| Solution Path | Path to the .sln file (leave empty to auto-detect) |

## Usage

1. Ensure configurations are correct.
2. Click the **🐞 Run + Attach Debug** icon in the Godot Editor toolbar (or press `Alt+F5`).
3. The plugin will automatically:
   - Run the project.
   - Detect the Godot game process PID.
   - Launch your IDE and attach the debugger to that PID.

## Waiting for Debugger (Autoload)

To ensure you don't miss breakpoints during initialization (e.g., `_Ready`), the plugin automatically registers a `DebugWait` autoload when enabled.

When the plugin is active:
- The game will pause at startup, showing a **"Waiting for debugger..."** overlay.
- It automatically resumes once the debugger attaches.
- You can press **ESC** to skip waiting.
- It times out and resumes automatically after 30 seconds.

## IDE Support

### VS Code
- Automatically generates/updates `.vscode/launch.json`.
- Requires the C# extension.
- Automatically sends `F5` to the VS Code window to start debugging.

## Troubleshooting

### Process Not Found (PID)
- Ensure the project is built with C#.
- Try increasing the **Attach Delay Ms**.

### VS Code Fails to Attach
- Ensure the C# extension is installed.
- Manually select the **".NET Attach (Godot)"** configuration in VS Code.

## Known Limitations

- **Restart Godot After Debugging**: Due to a known issue [Godot #78513](https://github.com/godotengine/godot/issues/78513), reloading .NET assemblies often fails after a debug session, causing errors on the next run. The plugin will show a reminder popup if this error is detected, suggesting a restart.
- **Windows Only**: Currently uses WMI for process detection, so only Windows is supported.
