# Changelog

All notable changes to this project will be documented in this file.

## [2.0.0] - 2025-12-18

### Added

- **New Architecture**: Separated into GDScript Plugin + standalone .NET Debug Attach Service
- **CliWrap**: Use CliWrap library for cleaner process execution
- **Show Service Console Setting**: Option to show/hide the Debug Attach Service CMD window
- **Multiple F5 Sends**: Send F5 twice to improve reliability

### Changed

- **GDScript Plugin**: Rewrote the Godot Editor plugin in pure GDScript to avoid C# assembly reload issues
- **Synchronous Debugger Wait**: DebugWaitAutoload now uses synchronous blocking (`Thread.Sleep`) instead of `_Process` polling to ensure other nodes' `_Ready` doesn't execute before debugger attaches
- **Auto-Register Autoload**: Plugin automatically registers DebugWaitAutoload when enabled

### Fixed

- **Assembly Reload Issues**: Resolved issues caused by C# assembly reloads during plugin enable/disable
- **F5 Reliability**: Improved F5 keypress reliability with multiple sends and better window activation

### Technical Details

- Debug Attach Service runs as an independent .NET 8 console application
- Communication between Plugin and Service via TCP (127.0.0.1:47632)
- Service handles: PID detection, IDE path detection, launch.json creation, IDE launching, F5 keypress

---

## [1.0.0] - Initial Release

### Features

- One-click Run + Attach Debug
- Support for VS Code, Cursor, and AntiGravity
- Auto-detect IDE and solution paths
- Keyboard shortcut: Alt+F5
- Optional wait for debugger (DebugWaitAutoload)
