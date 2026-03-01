using System.Text.Json.Serialization;

namespace SapAdapter.Models;

// ─── Session Info ──────────────────────────────────────────────────────
public record SapSessionInfo
{
    [JsonPropertyName("systemId")] public string SystemId { get; init; } = "";
    [JsonPropertyName("client")] public string Client { get; init; } = "";
    [JsonPropertyName("user")] public string User { get; init; } = "";
    [JsonPropertyName("language")] public string Language { get; init; } = "";
    [JsonPropertyName("server")] public string Server { get; init; } = "";
    [JsonPropertyName("scriptingModeReadOnly")] public bool ScriptingModeReadOnly { get; init; }
    [JsonPropertyName("transaction")] public string Transaction { get; init; } = "";
    [JsonPropertyName("program")] public string? Program { get; init; }
    [JsonPropertyName("dynpro")] public string? Dynpro { get; init; }
    [JsonPropertyName("screenNumber")] public string ScreenNumber { get; init; } = "";
}

// ─── Status Bar ────────────────────────────────────────────────────────
public record SapStatusBar
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
    [JsonPropertyName("msgId")] public string MsgId { get; init; } = "";
    [JsonPropertyName("msgNo")] public string MsgNo { get; init; } = "";
    [JsonPropertyName("params")] public List<string> Params { get; init; } = new();
}

// ─── Field Value ───────────────────────────────────────────────────────
public record SapFieldValue
{
    [JsonPropertyName("value")] public object? Value { get; init; }
    [JsonPropertyName("raw")] public string? Raw { get; init; }
    [JsonPropertyName("editable")] public bool? Editable { get; init; }
    [JsonPropertyName("visible")] public bool? Visible { get; init; }
    [JsonPropertyName("kind")] public string? Kind { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
}

// ─── Entity References ─────────────────────────────────────────────────
public record SapEntityRef
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("evidence")] public List<string> Evidence { get; init; } = new();
    [JsonPropertyName("labels")] public Dictionary<string, string>? Labels { get; init; }
}

public record SapInvoiceEntity
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("amount")] public double? Amount { get; init; }
    [JsonPropertyName("currency")] public string? Currency { get; init; }
    [JsonPropertyName("evidence")] public List<string> Evidence { get; init; } = new();
}

public record SapEntityBundle
{
    [JsonPropertyName("po")] public SapEntityRef? Po { get; init; }
    [JsonPropertyName("vendor")] public SapEntityRef? Vendor { get; init; }
    [JsonPropertyName("invoice")] public SapInvoiceEntity? Invoice { get; init; }
    [JsonPropertyName("materialDoc")] public SapEntityRef? MaterialDoc { get; init; }
}

// ─── Extraction Report ─────────────────────────────────────────────────
public record ShellInfo
{
    [JsonPropertyName("shellId")] public string ShellId { get; init; } = "";
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; init; } = new();
    [JsonPropertyName("summaryAvailable")] public bool SummaryAvailable { get; init; }
}

public record BudgetReport
{
    [JsonPropertyName("truncatedTree")] public bool? TruncatedTree { get; init; }
    [JsonPropertyName("maxCellReadsHit")] public bool? MaxCellReadsHit { get; init; }
    [JsonPropertyName("elapsedMs")] public long ElapsedMs { get; init; }
}

public record SapExtractionReport
{
    [JsonPropertyName("packMatched")] public string? PackMatched { get; init; }
    [JsonPropertyName("extractorsRun")] public List<string> ExtractorsRun { get; init; } = new();
    [JsonPropertyName("shells")] public List<ShellInfo> Shells { get; init; } = new();
    [JsonPropertyName("budgets")] public BudgetReport Budgets { get; init; } = new();
    [JsonPropertyName("warnings")] public List<string> Warnings { get; init; } = new();
}

public record SnapshotDebug
{
    [JsonPropertyName("extractionReport")] public SapExtractionReport? ExtractionReport { get; init; }
}
