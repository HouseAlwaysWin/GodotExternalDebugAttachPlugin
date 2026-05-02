using System.Text.Json.Serialization;

namespace DebugAttachService;

/// <summary>
/// IPC message format for debug attach requests
/// </summary>
public record AttachRequest
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "debug-attach-request";

    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "godot";

    [JsonPropertyName("editor")]
    public string Editor { get; init; } = "vscode";

    [JsonPropertyName("editorPath")]
    public string? EditorPath { get; init; }

    [JsonPropertyName("workspacePath")]
    public string? WorkspacePath { get; init; }

    /// <summary>
    /// Max F5 rounds while verifying debugger attach on the game PID (from Godot Project Settings).
    /// When null, <c>DEBUG_ATTACH_F5_ATTACH_CHECK_MAX</c> applies.
    /// </summary>
    [JsonPropertyName("f5AttachCheckMax")]
    public int? F5AttachCheckMax { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Response from the service after processing a request
/// </summary>
public record AttachResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }
}
