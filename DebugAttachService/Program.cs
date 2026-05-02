using DebugAttachService;

// Parse command line arguments
var port = TcpAttachServer.DefaultPort;
var commandArgs = args; // args is provided by top-level statements

for (int i = 0; i < commandArgs.Length; i++)
{
    if ((commandArgs[i] == "--port" || commandArgs[i] == "-p") && i + 1 < commandArgs.Length)
    {
        if (int.TryParse(commandArgs[i + 1], out var parsedPort))
        {
            port = parsedPort;
        }
    }
    else if (commandArgs[i] == "--help" || commandArgs[i] == "-h")
    {
        ConsoleLog.WriteLine("Debug Attach Service - Godot C# Debugger Helper");
        ConsoleLog.WriteLine();
        ConsoleLog.WriteLine("Usage: DebugAttachService [options]");
        ConsoleLog.WriteLine();
        ConsoleLog.WriteLine("Options:");
        ConsoleLog.WriteLine("  -p, --port <port>  TCP port to listen on (default: 47632)");
        ConsoleLog.WriteLine("  -h, --help         Show this help message");
        ConsoleLog.WriteLine();
        ConsoleLog.WriteLine("The service listens for debug attach requests from Godot Editor Plugin");
        ConsoleLog.WriteLine("and triggers the appropriate IDE debugger to attach to the game process.");
        ConsoleLog.WriteLine();
        ConsoleLog.WriteLine("Environment (optional):");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_MAX_WAIT_PROCESS_MS=…  Max wait for IDE process (default 60000, 5–120000)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_MIN_IDE_MS=…          Min wait after process exists before ready poll (overrides heuristics)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_IDE_TITLE_WAIT_MAX_MS=…  Max wait for workspace.json notify or title (0=skip)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_IDE_READY_POLL_MS=…      Title/storage re-check interval when waiting (default 250)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_POST_READY_SETTLE_MS=…  After first ready signal, wait before SendKeys (cold Cursor default ~5500)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_WORKSPACE_STORAGE_ROOT=…  Override path to …User\\workspaceStorage (Cursor/VS Code)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_PRE_SENDKEYS_DELAY_MS=…  Extra delay before SendKeys (default 2500)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_MINIMAL_PRE_F5_WAIT=0   Restore long title/workspace wait (default on with F5-until-attached)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_F5_UNTIL_ATTACHED=0         Disable: retry F5 until game PID has debugger (default on)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_F5_ATTACH_CHECK_MAX=…    Max F5 rounds when until-attached (default 12, up to 100)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_F5_ATTACH_CHECK_DELAY_MS=… ms after each F5 before CheckRemoteDebuggerPresent (default 1800)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_TRY_CLI_DEBUG_START=1   Experimental: run --command before SendKeys");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_SKIP_F5_FALLBACK=1      Skip SendKeys (attach will usually not start)");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_START_DEBUG_KEYS=…    SendKeys for Start Debugging (default {F5}; e.g. {F8}, ^{F5})");
        ConsoleLog.WriteLine("  NO_COLOR / DEBUG_ATTACH_NO_COLOR   Disable colored output");
        ConsoleLog.WriteLine("  DEBUG_ATTACH_FORCE_COLOR=1        Force colors when stdout is redirected");
        return 0;
    }
}

ConsoleLog.WriteStartupBanner();

// Setup cancellation for Ctrl+C
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    ConsoleLog.WriteLine("\n[DebugAttachService] Shutting down...");
    e.Cancel = true;
    cts.Cancel();
};

// Start the server
using var server = new TcpAttachServer(port);

try
{
    await server.StartAsync(cts.Token);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    ConsoleLog.WriteErrorLine($"[DebugAttachService] Fatal error: {ex.Message}");
    return 1;
}

ConsoleLog.WriteLine("[DebugAttachService] Service stopped.");
return 0;
