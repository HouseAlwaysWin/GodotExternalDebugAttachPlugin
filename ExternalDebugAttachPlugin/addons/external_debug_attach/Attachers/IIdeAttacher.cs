namespace ExternalDebugAttach;

/// <summary>
/// Result of an attach operation
/// </summary>
public class AttachResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static AttachResult Ok() => new() { Success = true };
    public static AttachResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Interface for IDE attacher implementations
/// </summary>
public interface IIdeAttacher
{
    /// <summary>
    /// Attach the IDE debugger to the specified process
    /// </summary>
    /// <param name="pid">Process ID to attach to</param>
    /// <param name="idePath">Path to the IDE executable</param>
    /// <param name="solutionPath">Path to the solution or workspace</param>
    /// <returns>Result indicating success or failure</returns>
    AttachResult Attach(int pid, string idePath, string solutionPath);
}
