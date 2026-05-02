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
    /// <param name="f5AttachCheckMax">
    /// Optional max F5 rounds when verifying debugger attach (overrides DEBUG_ATTACH_F5_ATTACH_CHECK_MAX env).
    /// </param>
    /// <returns>Result of the attach operation</returns>
    AttachResult Attach(int pid, string idePath, string workspacePath, int? f5AttachCheckMax = null);
}
