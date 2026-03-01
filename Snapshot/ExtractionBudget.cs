namespace SapAdapter.Snapshot;

/// <summary>
/// Tracks extraction budgets to prevent runaway COM calls.
/// Limits time, node count, and cell reads during snapshot extraction.
/// </summary>
public class ExtractionBudget
{
    public long StartTime { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public int MaxDurationMs { get; init; } = 4000;
    public int MaxNodes { get; init; } = 5000;
    public int NodeCount { get; set; }
    public int MaxCellReads { get; init; } = 500;
    public int CellReadCount { get; set; }
    public bool MaxCellReadsHit { get; set; }

    public bool IsWithinBudget()
    {
        long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - StartTime;
        if (elapsed > MaxDurationMs) return false;
        if (NodeCount > MaxNodes) return false;
        if (CellReadCount > MaxCellReads)
        {
            MaxCellReadsHit = true;
            return false;
        }
        return true;
    }

    public long ElapsedMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - StartTime;
}

/// <summary>
/// Shared context passed through all extractors during a snapshot capture.
/// </summary>
public class ExtractorContext
{
    public required dynamic Session { get; init; }
    public required ExtractionBudget Budget { get; init; }
    public List<string> ExtractorsRun { get; } = new();
    public List<Models.ShellInfo> Shells { get; } = new();
    public List<string> Warnings { get; } = new();
    public string? PackMatched { get; set; }
}
