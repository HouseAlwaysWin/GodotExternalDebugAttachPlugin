# External Debug Attach Plugin

**English** | [中文](README_zh_CN.md)

One-click Run + Attach Debug to external IDE for Godot Editor.

## Architecture

This plugin uses a **two-component architecture** to avoid C# assembly reload issues:

| Component                | Language | Description                                                                  |
| ------------------------ | -------- | ---------------------------------------------------------------------------- |
| **Editor Plugin**        | GDScript | Handles UI, settings, and communication                                      |
| **Debug Attach Service** | .NET 8   | Standalone process for PID detection, IDE launching, and debugger attachment |

## Features

- 🚀 One-click to run game and attach debugger
- 🔧 Supports **VS Code**, **Cursor**, and **AntiGravity**
- ⏳ Built-in wait for debugger (never miss `_Ready` breakpoints)
- 🎯 Auto-detect IDE paths and game process PID
- ⌨️ Keyboard shortcut: **Alt+F5**
- 🖥️ Optional Service console window for debugging

## Installation

1. Copy the `addons/external_debug_attach/` folder to your Godot project.
2. Rebuild your C# project (ensure the plugin compiles successfully).
3. In Godot Editor: Go to **Project** → **Project Settings** → **Plugins**.
4. Enable the **"External Debug Attach"** plugin.

## Configuration

Go to **Project** → **Project Settings** → **General** → **Dotnet** → **External Debug Attach**:

| Setting              | Description                                                 |
| -------------------- | ----------------------------------------------------------- |
| IDE Type             | Select IDE: VSCode, Cursor, or AntiGravity                  |
| VS Code Path         | Path to VS Code executable (leave empty to auto-detect)     |
| Cursor Path          | Path to Cursor executable (leave empty to auto-detect)      |
| AntiGravity Path     | Path to AntiGravity executable (leave empty to auto-detect) |
| Show Service Console | Show Debug Attach Service console window (for debugging)    |
| Auto register Debugwait Autoload | Register `DebugWait` autoload (recommended on)   |
| Debug Wait Seconds  | Main-thread wait before the main scene loads (for `_Ready` breakpoints) |

### Show Service Console Window

To view the Debug Attach Service logs (useful for troubleshooting), enable the console window:

1. Go to **Project** → **Project Settings** → **Dotnet** → **External Debug Attach**
2. Set **Show Service Console** to **true**

When enabled, pressing Alt+F5 will open a CMD window displaying:

- TCP server status
- Received request details
- PID detection results
- IDE launch status
- F5 keypress status

> **Tip**: If you accidentally close the Service window, you can restart it by **disabling and re-enabling the plugin**:
>
> 1. Go to **Project** → **Project Settings** → **Plugins**
> 2. Uncheck **External Debug Attach**
> 3. Check **External Debug Attach** again

## Usage

1. Ensure configurations are correct.
2. Click the **🐞 Run + Attach Debug** icon in the Godot Editor toolbar (or press `Alt+F5`).
3. The plugin will automatically:
   - Start the Debug Attach Service (if not running)
   - Run the project
   - Pause the game and wait for debugger (via DebugWaitAutoload)
   - Detect the Godot game process PID
   - Launch your IDE and attach the debugger

## How It Works

1. **GDScript Plugin** sends an attach request via TCP to the Service
2. **Debug Attach Service** (independent .NET process):
   - Scans for Godot/dotnet game processes
   - Auto-detects IDE installation path
   - Creates/updates `.vscode/launch.json`
   - Launches the IDE with the workspace
   - Sends F5 keypress to start debugging
3. **DebugWaitAutoload** (in game process):
   - Pauses the game with a visual overlay
   - Waits for debugger to attach (synchronous blocking)
   - Resumes when debugger connects

## DebugWaitAutoload

The plugin automatically registers `DebugWaitAutoload` when enabled. This ensures you don't miss breakpoints during initialization (e.g., `_Ready`).

When the game starts:

- Shows a **"Waiting for debugger..."** overlay
- Automatically resumes once the debugger attaches
- Press **ESC** to skip waiting
- Times out after 30 seconds

## IDE Support

### VS Code

- Automatically generates/updates `.vscode/launch.json`
- Requires the C# extension
- Automatically sends `F5` to start debugging

### Cursor

- Same as VS Code (uses the same debugger configuration)
- Automatically detects Cursor installation

### AntiGravity

- Same as VS Code (uses the same debugger configuration)
- Automatically detects AntiGravity installation

## Troubleshooting

### Service Not Starting

- Check if another instance is already running
- Enable **Show Service Console** to see error messages
- Manually run `StartService.bat` to diagnose issues

### Process Not Found (PID)

- Ensure the project is built with C#
- The Service auto-retries up to 10 times

### IDE Fails to Attach

- Ensure the C# extension is installed in your IDE
- Manually select the **".NET Attach (Godot)"** configuration

## Known Limitations

- **Windows Only**: Currently only Windows is supported

## License

MIT License
