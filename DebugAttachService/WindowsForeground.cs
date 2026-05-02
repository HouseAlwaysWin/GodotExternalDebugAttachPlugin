using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DebugAttachService;

/// <summary>
/// Win32 helpers so SendKeys targets the IDE window (Electron often has 0 MainWindowHandle on the root process).
/// </summary>
internal static class WindowsForeground
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Try to bring any visible top-level window for this process to the foreground.
    /// </summary>
    public static bool TryActivateProcessWindows(int processId)
    {
        IntPtr picked = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if ((int)pid != processId || !IsWindowVisible(hWnd))
                return true;
            if (GetWindowTextLength(hWnd) < 1)
                return true;
            picked = hWnd;
            return true;
        }, IntPtr.Zero);

        if (picked == IntPtr.Zero)
            return false;

        // SW_RESTORE on a *maximized* window restores to normal size (looks like the IDE "shrinks").
        // Only restore when minimized; otherwise bring forward without changing maximize state.
        if (IsIconic(picked))
            ShowWindow(picked, SW_RESTORE);
        // IsZoomed == maximized: SetForegroundWindow only

        return SetForegroundWindow(picked);
    }

    /// <summary>
    /// Activate any candidate window for processes matching <paramref name="processName"/> (e.g. Code, Cursor).
    /// </summary>
    public static void TryActivateAnyNamedProcess(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                if (TryActivateProcessWindows(p.Id))
                    return;
            }
            catch
            {
                // ignore per-process errors
            }
        }
    }
}
