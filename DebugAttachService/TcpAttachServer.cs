using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DebugAttachService;

/// <summary>
/// TCP Server that listens for debug attach requests
/// </summary>
public class TcpAttachServer : IDisposable
{
    private readonly int _port;
    private readonly Action<string> _log;
    private readonly Action<string> _logError;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public const int DefaultPort = 47632;

    public TcpAttachServer(int port = DefaultPort, Action<string>? log = null, Action<string>? logError = null)
    {
        _port = port;
        _log = log ?? Console.WriteLine;
        _logError = logError ?? Console.Error.WriteLine;
    }

    /// <summary>
    /// Start the TCP server and listen for connections
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, _port);

        try
        {
            _listener.Start();
            _log($"[DebugAttachService] TCP Server listening on 127.0.0.1:{_port}");

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleClientAsync(client, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logError($"[DebugAttachService] Error accepting client: {ex.Message}");
                }
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _logError($"[DebugAttachService] Port {_port} is already in use. Another instance may be running.");
            throw;
        }
        finally
        {
            _listener?.Stop();
        }
    }

    /// <summary>
    /// Stop the TCP server
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                // Read request line by line until we get a complete JSON
                var requestJson = await reader.ReadLineAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(requestJson))
                {
                    _log("[DebugAttachService] Received empty request");
                    return;
                }

                _log($"[DebugAttachService] Received request: {requestJson}");

                // Parse the request
                AttachRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<AttachRequest>(requestJson);
                }
                catch (JsonException ex)
                {
                    _logError($"[DebugAttachService] Invalid JSON: {ex.Message}");
                    var errorResponse = new AttachResponse
                    {
                        Success = false,
                        Message = "Invalid JSON format",
                        ErrorCode = "INVALID_JSON"
                    };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(errorResponse));
                    return;
                }

                if (request == null)
                {
                    var errorResponse = new AttachResponse
                    {
                        Success = false,
                        Message = "Request is null",
                        ErrorCode = "NULL_REQUEST"
                    };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(errorResponse));
                    return;
                }

                // Process the attach request
                var response = await ProcessAttachRequestAsync(request, cancellationToken);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                _logError($"[DebugAttachService] Error handling client: {ex.Message}");
            }
        }
    }

    private async Task<AttachResponse> ProcessAttachRequestAsync(AttachRequest request, CancellationToken cancellationToken)
    {
        var pid = request.Pid;
        _log($"[DebugAttachService] Processing attach request for PID {pid}, Editor: {request.Editor}");

        // If PID is 0, auto-detect the game process
        if (pid <= 0)
        {
            _log("[DebugAttachService] PID is 0, auto-detecting game process...");

            // Retry mechanism for PID detection
            const int maxRetries = 10;
            const int retryDelayMs = 500;

            for (int i = 0; i < maxRetries && !cancellationToken.IsCancellationRequested; i++)
            {
                pid = ProcessScanner.FindGodotProcessPid(_log);
                if (pid > 0)
                {
                    _log($"[DebugAttachService] Auto-detected game PID: {pid}");
                    break;
                }

                _log($"[DebugAttachService] Game process not found, retrying... ({i + 1}/{maxRetries})");
                await Task.Delay(retryDelayMs, cancellationToken);
            }

            if (pid <= 0)
            {
                return new AttachResponse
                {
                    Success = false,
                    Message = $"Failed to auto-detect game process after {maxRetries} retries",
                    ErrorCode = "PROCESS_NOT_FOUND"
                };
            }
        }
        else
        {
            // Validate provided PID
            if (!IsProcessRunning(pid))
            {
                // Retry mechanism: wait and check again
                const int maxRetries = 5;
                const int retryDelayMs = 500;
                bool found = false;

                for (int i = 0; i < maxRetries && !cancellationToken.IsCancellationRequested; i++)
                {
                    _log($"[DebugAttachService] PID {pid} not found, retrying... ({i + 1}/{maxRetries})");
                    await Task.Delay(retryDelayMs, cancellationToken);

                    if (IsProcessRunning(pid))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return new AttachResponse
                    {
                        Success = false,
                        Message = $"Process with PID {pid} not found after {maxRetries} retries",
                        ErrorCode = "PROCESS_NOT_FOUND"
                    };
                }
            }
        }

        _log($"[DebugAttachService] Found process with PID {pid}");

        // Get the appropriate attacher
        IIdeAttacher attacher = request.Editor.ToLowerInvariant() switch
        {
            "vscode" => new VSCodeAttacher(_log, _logError),
            "cursor" => new VSCodeAttacher(_log, _logError),
            "antigravity" => new VSCodeAttacher(_log, _logError),
            _ => new VSCodeAttacher(_log, _logError) // Default to VS Code
        };

        // Auto-detect IDE path if not provided
        var editorPath = request.EditorPath;
        if (string.IsNullOrEmpty(editorPath))
        {
            _log($"[DebugAttachService] Editor path not provided, auto-detecting for {request.Editor}...");
            editorPath = IdePathDetector.DetectIdePath(request.Editor);
            if (!string.IsNullOrEmpty(editorPath))
            {
                _log($"[DebugAttachService] Auto-detected IDE path: {editorPath}");
            }
            else
            {
                _logError("[DebugAttachService] Failed to auto-detect IDE path");
                return new AttachResponse
                {
                    Success = false,
                    Message = $"Failed to auto-detect IDE path for {request.Editor}",
                    ErrorCode = "IDE_NOT_FOUND"
                };
            }
        }

        // Perform the attach
        var result = attacher.Attach(
            pid,
            editorPath,
            request.WorkspacePath ?? ""
        );

        return new AttachResponse
        {
            Success = result.Success,
            Message = result.Success ? "Attach initiated successfully" : result.ErrorMessage,
            ErrorCode = result.Success ? null : "ATTACH_FAILED"
        };
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
    }
}
