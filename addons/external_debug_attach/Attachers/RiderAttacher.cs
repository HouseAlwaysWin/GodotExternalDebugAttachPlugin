using System;
using System.Diagnostics;
using System.IO;
using Godot;

namespace ExternalDebugAttach;

/// <summary>
/// Attacher implementation for JetBrains Rider
/// Uses the Rider CLI command: rider attach-to-process [debugger-key] <pid> [solution-path]
/// </summary>
public class RiderAttacher : IIdeAttacher
{
    public AttachResult Attach(int pid, string idePath, string solutionPath)
    {
        try
        {
            // Validate IDE path
            if (string.IsNullOrEmpty(idePath) || !File.Exists(idePath))
            {
                return AttachResult.Fail($"Rider executable not found at: {idePath}");
            }

            // Validate solution path
            if (string.IsNullOrEmpty(solutionPath) || !File.Exists(solutionPath))
            {
                return AttachResult.Fail($"Solution file not found at: {solutionPath}");
            }

            // Build command arguments
            // Format: rider attach-to-process netcore <pid> <solution-path>
            var arguments = $"attach-to-process netcore {pid} \"{solutionPath}\"";

            GD.Print($"[RiderAttacher] Executing: \"{idePath}\" {arguments}");

            // Start Rider with attach command
            var startInfo = new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process == null)
            {
                return AttachResult.Fail("Failed to start Rider process");
            }

            // Wait a bit for Rider to start attaching
            process.WaitForExit(5000);

            // Check for errors
            var stderr = process.StandardError.ReadToEnd();
            if (!string.IsNullOrEmpty(stderr))
            {
                GD.PrintErr($"[RiderAttacher] stderr: {stderr}");
                // Don't fail immediately - Rider might still have attached successfully
            }

            var stdout = process.StandardOutput.ReadToEnd();
            if (!string.IsNullOrEmpty(stdout))
            {
                GD.Print($"[RiderAttacher] stdout: {stdout}");
            }

            GD.Print($"[RiderAttacher] Attach command sent to Rider");
            return AttachResult.Ok();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RiderAttacher] Exception: {ex.Message}");
            return AttachResult.Fail($"Exception: {ex.Message}");
        }
    }
}
