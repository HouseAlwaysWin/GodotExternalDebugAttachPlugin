using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Godot;

namespace ExternalDebugAttach;

/// <summary>
/// Attacher implementation for Visual Studio Code
/// Creates/updates launch.json with attach configuration and opens VS Code
/// </summary>
public class VSCodeAttacher : IIdeAttacher
{
    public AttachResult Attach(int pid, string idePath, string solutionPath)
    {
        try
        {
            // Validate IDE path
            if (string.IsNullOrEmpty(idePath) || !File.Exists(idePath))
            {
                return AttachResult.Fail($"VS Code executable not found at: {idePath}");
            }

            // Get workspace path - prefer solution directory, fallback to project path
            var projectPath = ProjectSettings.GlobalizePath("res://");
            string workspacePath;

            if (!string.IsNullOrEmpty(solutionPath) && File.Exists(solutionPath))
            {
                workspacePath = Path.GetDirectoryName(solutionPath) ?? projectPath;
            }
            else
            {
                workspacePath = projectPath;
            }

            GD.Print($"[VSCodeAttacher] Workspace path: {workspacePath}");

            if (string.IsNullOrEmpty(workspacePath))
            {
                return AttachResult.Fail("Could not determine workspace path");
            }

            // Create .vscode directory if it doesn't exist
            var vscodePath = Path.Combine(workspacePath, ".vscode");
            Directory.CreateDirectory(vscodePath);

            // Create or update launch.json with attach configuration
            var launchJsonPath = Path.Combine(vscodePath, "launch.json");
            CreateLaunchJson(launchJsonPath, pid);

            GD.Print($"[VSCodeAttacher] Created launch.json at: {launchJsonPath}");

            // Open VS Code with the workspace
            var arguments = $"\"{workspacePath}\"";

            GD.Print($"[VSCodeAttacher] Executing: \"{idePath}\" {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = arguments,
                UseShellExecute = true
            };

            Process.Start(startInfo);

            GD.Print("[VSCodeAttacher] VS Code launched. Please start the '.NET Attach' debug configuration manually.");

            return AttachResult.Ok();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VSCodeAttacher] Exception: {ex.Message}");
            return AttachResult.Fail($"Exception: {ex.Message}");
        }
    }

    private void CreateLaunchJson(string launchJsonPath, int pid)
    {
        var launchConfig = new
        {
            version = "0.2.0",
            configurations = new[]
            {
                new
                {
                    name = ".NET Attach (Godot)",
                    type = "coreclr",
                    request = "attach",
                    processId = pid.ToString()
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(launchConfig, options);
        File.WriteAllText(launchJsonPath, json);
    }
}
