using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DebugAttachService;

/// <summary>
/// Win32 helpers so SendKeys targets the IDE window (Electron often has 0 MainWindowHandle on the root process).
/// Background processes cannot use bare SetForegroundWindow; use AttachThreadInput + BringWindowToTop (see TryForceForegroundWindow).
/// </summary>
internal static class WindowsForeground
{
    private const int SW_RESTORE = 9;

    private const uint AsfwAny = unchecked((uint)-1);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

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
    /// Windows blocks SetForegroundWindow unless foreground rules are satisfied; this pattern is the usual workaround.
    /// </summary>
    public static bool TryForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        _ = AllowSetForegroundWindow(AsfwAny);

        if (GetForegroundWindow() == hWnd)
            return true;

        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);

        uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint appThread = GetWindowThreadProcessId(hWnd, out _);
        uint currentThread = GetCurrentThreadId();

        bool a1 = false;
        bool a2 = false;

        try
        {
            if (appThread != currentThread)
                a1 = AttachThreadInput(currentThread, appThread, true);
            if (foreThread != appThread && foreThread != 0)
                a2 = AttachThreadInput(foreThread, appThread, true);

            BringWindowToTop(hWnd);
            return SetForegroundWindow(hWnd);
        }
        finally
        {
            if (a2)
                AttachThreadInput(foreThread, appThread, false);
            if (a1)
                AttachThreadInput(currentThread, appThread, false);
        }
    }

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

        if (IsIconic(picked))
            ShowWindow(picked, SW_RESTORE);

        return TryForceForegroundWindow(picked);
    }

    /// <summary>
    /// Prefer a window whose title contains the workspace folder name; otherwise longest title.
    /// </summary>
    public static bool TryActivateBestIdeWindow(string processName, string? workspaceFolderName, out int activatedProcessId)
    {
        activatedProcessId = 0;
        var validPids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                validPids.Add(p.Id);
            }
            finally
            {
                p.Dispose();
            }
        }

        if (validPids.Count == 0)
            return false;

        var hint = workspaceFolderName?.Trim();
        var hasHint = !string.IsNullOrEmpty(hint);

        IntPtr bestHwnd = IntPtr.Zero;
        var bestScore = -1;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (!validPids.Contains((int)windowPid) || !IsWindowVisible(hWnd))
                return true;

            var len = GetWindowTextLength(hWnd);
            if (len < 1)
                return true;

            var sb = new StringBuilder(512);
            _ = GetWindowText(hWnd, sb, 512);
            var title = sb.ToString();

            int score;
            if (hasHint && title.Contains(hint!, StringComparison.OrdinalIgnoreCase))
                score = 1_000_000 + title.Length;
            else
                score = title.Length;

            if (score > bestScore)
            {
                bestScore = score;
                bestHwnd = hWnd;
            }

            return true;
        }, IntPtr.Zero);

        if (bestHwnd == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(bestHwnd, out uint chosenPid);
        activatedProcessId = (int)chosenPid;

        return TryForceForegroundWindow(bestHwnd);
    }

    /// <summary>
    /// True when any visible top-level window for <paramref name="processName"/> has <paramref name="substring"/> in its title
    /// (used to wait until the IDE has opened the workspace before SendKeys).
    /// </summary>
    public static bool AnyIdeWindowTitleContains(string processName, string substring)
    {
        if (string.IsNullOrWhiteSpace(substring))
            return false;

        var needle = substring.Trim();
        var validPids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                validPids.Add(p.Id);
            }
            finally
            {
                p.Dispose();
            }
        }

        if (validPids.Count == 0)
            return false;

        var found = false;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (!validPids.Contains((int)windowPid) || !IsWindowVisible(hWnd))
                return true;

            if (GetWindowTextLength(hWnd) < 1)
                return true;

            var sb = new StringBuilder(512);
            _ = GetWindowText(hWnd, sb, 512);
            if (sb.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
