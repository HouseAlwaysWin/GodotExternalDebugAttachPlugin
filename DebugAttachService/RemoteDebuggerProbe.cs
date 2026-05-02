using System.Runtime.InteropServices;

namespace DebugAttachService;

/// <summary>
/// Detects whether a remote process has a debugger attached (used after F5 to confirm CoreCLR attach).
/// </summary>
internal static class RemoteDebuggerProbe
{
    private const uint ProcessQueryInformation = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool pbDebuggerPresent);

    /// <summary>
    /// Returns false if the handle could not be opened or the API failed; check <paramref name="error"/> or Win32 error.
    /// </summary>
    public static bool TryQueryDebuggerAttached(int processId, out bool debuggerAttached, out string? error)
    {
        debuggerAttached = false;
        error = null;

        if (processId <= 0)
        {
            error = "invalid process id";
            return false;
        }

        var h = OpenProcess(ProcessQueryInformation, false, processId);
        if (h == IntPtr.Zero)
        {
            error = $"OpenProcess failed (Win32 {Marshal.GetLastWin32Error()})";
            return false;
        }

        try
        {
            var flag = false;
            if (!CheckRemoteDebuggerPresent(h, ref flag))
            {
                error = $"CheckRemoteDebuggerPresent failed (Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }

            debuggerAttached = flag;
            return true;
        }
        finally
        {
            _ = CloseHandle(h);
        }
    }
}
