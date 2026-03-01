using System.Text.Json.Serialization;

namespace SapAdapter.Models;

/// <summary>
/// Full SAP screen snapshot. Uses class (not record) because screen packs
/// mutate Entities during the extraction pipeline.
/// Wire-compatible with the TypeScript SapScreenSnapshot interface.
/// </summary>
public class SapScreenSnapshot
{
    [JsonPropertyName("snapshotId")] public string SnapshotId { get; set; } = "";
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("capturedAt")] public string CapturedAt { get; set; } = "";
    [JsonPropertyName("sessionInfo")] public SapSessionInfo SessionInfo { get; set; } = new();
    [JsonPropertyName("window")] public WindowInfo Window { get; set; } = new();
    [JsonPropertyName("statusBar")] public SapStatusBar StatusBar { get; set; } = new();
    [JsonPropertyName("fields")] public Dictionary<string, object> Fields { get; set; } = new();
    [JsonPropertyName("entities")] public SapEntityBundle Entities { get; set; } = new();
    [JsonPropertyName("debug")] public SnapshotDebug? Debug { get; set; }
}

public record WindowInfo
{
    [JsonPropertyName("title")] public string Title { get; init; } = "";
}
