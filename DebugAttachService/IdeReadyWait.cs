using System.Diagnostics;

namespace DebugAttachService;

/// <summary>
/// Waits for IDE "open workspace" hints using OS file notifications (<see cref="FileSystemWatcher"/>)
/// plus a light title poll (no public Win32 event for arbitrary window title text without a message pump).
/// </summary>
internal static class IdeReadyWait
{
    /// <returns>True if a ready condition was met before <paramref name="maxMs"/>.</returns>
    public static bool WaitForFirstSignal(
        string processName,
        string workspacePath,
        string? workspaceFolderName,
        int maxMs,
        Action<string> log,
        out int elapsedMs,
        int pollIntervalMs)
    {
        var elapsedCapture = 0;
        var sw = Stopwatch.StartNew();

        bool IsReady()
        {
            if (WorkspaceStorageProbe.IsWorkspaceFolderRegistered(workspacePath, processName))
                return true;
            if (!string.IsNullOrWhiteSpace(workspaceFolderName)
                && WindowsForeground.AnyIdeWindowTitleContains(processName, workspaceFolderName))
                return true;
            return false;
        }

        if (IsReady())
        {
            elapsedCapture = (int)sw.ElapsedMilliseconds;
            elapsedMs = elapsedCapture;
            log($"[IdeReadyWait] Already ready at start ({elapsedMs}ms).");
            return true;
        }

        using var done = new ManualResetEventSlim(false);
        var signalLock = new object();
        FileSystemWatcher? watcher = null;

        void TrySignal(string reason)
        {
            if (done.IsSet)
                return;
            try
            {
                if (!IsReady())
                    return;
                lock (signalLock)
                {
                    if (done.IsSet)
                        return;
                    elapsedCapture = (int)sw.ElapsedMilliseconds;
                    log($"[IdeReadyWait] Ready ({reason}) after {elapsedCapture}ms (file notify + title poll).");
                    done.Set();
                }
            }
            catch
            {
                // Ignore transient IO during workspace.json write.
            }
        }

        void OnFsEvent(object _, FileSystemEventArgs e)
        {
            if (!e.FullPath.EndsWith("workspace.json", StringComparison.OrdinalIgnoreCase))
                return;
            TrySignal("workspace.json event");
        }

        void OnRenamed(object _, RenamedEventArgs e)
        {
            if (!e.FullPath.EndsWith("workspace.json", StringComparison.OrdinalIgnoreCase))
                return;
            TrySignal("workspace.json renamed");
        }

        var storageRoot = WorkspaceStorageProbe.GetWorkspaceStorageRootPath(processName);
        if (!string.IsNullOrEmpty(storageRoot))
        {
            try
            {
                Directory.CreateDirectory(storageRoot);
                watcher = new FileSystemWatcher(storageRoot)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    InternalBufferSize = 65536,
                };
                watcher.Created += OnFsEvent;
                watcher.Changed += OnFsEvent;
                watcher.Renamed += OnRenamed;
                watcher.Error += (_, err) =>
                    log($"[IdeReadyWait] FileSystemWatcher error: {err.GetException().Message}");
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                log($"[IdeReadyWait] Could not watch workspaceStorage ({storageRoot}): {ex.Message}");
                watcher?.Dispose();
                watcher = null;
            }
        }

        var pollCts = new CancellationTokenSource();
        var pollTask = Task.Run(() =>
        {
            var interval = Math.Clamp(pollIntervalMs, 50, 2000);
            while (!pollCts.IsCancellationRequested && !done.IsSet)
            {
                TrySignal("title/storage poll");
                try
                {
                    if (done.Wait(interval, pollCts.Token))
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, pollCts.Token);

        var signaled = done.Wait(TimeSpan.FromMilliseconds(maxMs));
        pollCts.Cancel();
        try
        {
            pollTask.Wait(2000);
        }
        catch
        {
            // Ignore cancellation unwind.
        }

        watcher?.Dispose();

        elapsedMs = signaled ? elapsedCapture : (int)sw.ElapsedMilliseconds;

        return signaled;
    }

    public static int GetReadyPollIntervalMs()
    {
        var raw = Environment.GetEnvironmentVariable("DEBUG_ATTACH_IDE_READY_POLL_MS")?.Trim();
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
            return Math.Clamp(ms, 50, 2000);
        return 250;
    }
}
