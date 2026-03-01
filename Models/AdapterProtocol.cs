using System.Text.Json.Serialization;

namespace SapAdapter.Models;

// ─── Adapter Request ──────────────────────────────────────────────────
public record AdapterRequest
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("payload")] public Dictionary<string, object?>? Payload { get; init; }
}

// ─── Adapter Response ─────────────────────────────────────────────────
public record AdapterResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("payload")] public object? Payload { get; init; }
    [JsonPropertyName("error")] public AdapterError? Error { get; init; }
}

public record AdapterError
{
    [JsonPropertyName("code")] public string Code { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("details")] public object? Details { get; init; }
}

// ─── Adapter Event ────────────────────────────────────────────────────
public record AdapterEvent
{
    [JsonPropertyName("type")] public string Type { get; init; } = "event";
    [JsonPropertyName("event")] public string Event { get; init; } = "";
    [JsonPropertyName("payload")] public object? Payload { get; init; }
}

// ─── Session List Item (returned by listSessions) ─────────────────────
public record SessionListItem
{
    [JsonPropertyName("sessionId")] public string SessionId { get; init; } = "";
    [JsonPropertyName("systemId")] public string SystemId { get; init; } = "";
    [JsonPropertyName("client")] public string Client { get; init; } = "";
    [JsonPropertyName("user")] public string User { get; init; } = "";
    [JsonPropertyName("transaction")] public string Transaction { get; init; } = "";
    [JsonPropertyName("windowTitle")] public string WindowTitle { get; init; } = "";
}
