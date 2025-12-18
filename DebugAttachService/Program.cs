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
        Console.WriteLine("Debug Attach Service - Godot C# Debugger Helper");
        Console.WriteLine();
        Console.WriteLine("Usage: DebugAttachService [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --port <port>  TCP port to listen on (default: 47632)");
        Console.WriteLine("  -h, --help         Show this help message");
        Console.WriteLine();
        Console.WriteLine("The service listens for debug attach requests from Godot Editor Plugin");
        Console.WriteLine("and triggers the appropriate IDE debugger to attach to the game process.");
        return 0;
    }
}

Console.WriteLine("========================================");
Console.WriteLine("  Debug Attach Service for Godot C#");
Console.WriteLine("========================================");
Console.WriteLine();

// Setup cancellation for Ctrl+C
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    Console.WriteLine("\n[DebugAttachService] Shutting down...");
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
    Console.Error.WriteLine($"[DebugAttachService] Fatal error: {ex.Message}");
    return 1;
}

Console.WriteLine("[DebugAttachService] Service stopped.");
return 0;
