namespace DebugAttachService;

/// <summary>
/// Result of an attach operation
/// </summary>
public record AttachResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static AttachResult Ok() => new() { Success = true };
    public static AttachResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Interface for IDE-specific attach implementations
/// </summary>
public interface IIdeAttacher
{
    /// <summary>
    /// Attach the debugger to the specified process
    /// </summary>
    /// <param name="pid">Process ID to attach to</param>
    /// <param name="idePath">Path to the IDE executable</param>
    /// <param name="workspacePath">Path to the workspace/solution</param>
    /// <returns>Result of the attach operation</returns>
    AttachResult Attach(int pid, string idePath, string workspacePath);
}
