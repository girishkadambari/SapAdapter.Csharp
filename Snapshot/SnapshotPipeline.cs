using SapAdapter.Com;
using SapAdapter.Snapshot.Extractors;
using SapAdapter.Snapshot.Packs;
using Serilog;

namespace SapAdapter.Snapshot;

/// <summary>
/// Main orchestrator for capturing a full screen snapshot from SAP GUI.
/// Coordinates extractors, applies screen packs, and builds the extraction report.
/// </summary>
public class SnapshotPipeline
{
    private static readonly ILogger Log = Serilog.Log.ForContext<SnapshotPipeline>();

    private readonly List<IScreenPack> _packs = new()
    {
        new MiroPack(),
        new Me23nPack()
    };

    private readonly int _maxDurationMs;
    private readonly int _maxNodes;
    private readonly int _maxCellReads;

    public SnapshotPipeline(int maxDurationMs = 4000, int maxNodes = 5000, int maxCellReads = 500)
    {
        _maxDurationMs = maxDurationMs;
        _maxNodes = maxNodes;
        _maxCellReads = maxCellReads;
    }

    /// <summary>
    /// Captures a complete screen snapshot from the specified SAP session.
    /// </summary>
    public Models.SapScreenSnapshot Capture(string sessionId, int connIndex, int sesIndex)
    {
        Log.Information("Capturing snapshot for session {SessionId} [{ConnIdx},{SesIdx}]",
            sessionId, connIndex, sesIndex);

        var session = SapEngine.GetSession(connIndex, sesIndex);

        var budget = new ExtractionBudget
        {
            MaxDurationMs = _maxDurationMs,
            MaxNodes = _maxNodes,
            MaxCellReads = _maxCellReads
        };

        var ctx = new ExtractorContext
        {
            Session = session,
            Budget = budget
        };

        var win = SafeCom.Execute(() => session.ActiveWindow, "get ActiveWindow");
        var info = SafeCom.Execute(() => session.Info, "get session info");

        var snapshotId = Guid.NewGuid().ToString("N").Substring(0, 21); // ~nanoid length

        var snapshot = new Models.SapScreenSnapshot
        {
            SnapshotId = snapshotId,
            SessionId = sessionId,
            CapturedAt = DateTime.UtcNow.ToString("o"),
            SessionInfo = new Models.SapSessionInfo
            {
                SystemId = SafeCom.Execute(() => (string)info.SystemName, "get SID"),
                Client = SafeCom.Execute(() => (string)info.Client, "get client"),
                User = SafeCom.Execute(() => (string)info.User, "get user"),
                Language = SafeCom.Execute(() => (string)info.Language, "get lang"),
                Server = SafeCom.Execute(() => { try { return (string)info.MessageServer; } catch { return ""; } }, "get server"),
                ScriptingModeReadOnly = false,
                Transaction = SafeCom.Execute(() => (string)info.Transaction, "get tx"),
                Program = SafeCom.Execute(() => { try { return (string)info.Program; } catch { return ""; } }, "get program"),
                ScreenNumber = SafeCom.Execute(() => info.ScreenNumber.ToString(), "get screen")
            },
            Window = new Models.WindowInfo
            {
                Title = SafeCom.Execute(() => (string)win.Text, "get win text")
            },
            StatusBar = StatusBarExtractor.Extract(ctx),
            Fields = FieldExtractor.Extract(ctx, SafeCom.Execute(() => win.FindById("usr"), "get usr area")),
            Entities = new Models.SapEntityBundle()
        };

        // Apply Screen Packs
        foreach (var pack in _packs)
        {
            if (pack.Match(snapshot))
            {
                ctx.PackMatched = pack.GetType().Name;
                Log.Information("Screen pack matched: {PackName}", ctx.PackMatched);
                pack.Apply(ctx, snapshot);
                break; // Only first match
            }
        }

        // Build extraction report
        snapshot.Debug = new Models.SnapshotDebug
        {
            ExtractionReport = new Models.SapExtractionReport
            {
                PackMatched = ctx.PackMatched,
                ExtractorsRun = ctx.ExtractorsRun,
                Shells = ctx.Shells,
                Budgets = new Models.BudgetReport
                {
                    MaxCellReadsHit = budget.MaxCellReadsHit,
                    ElapsedMs = budget.ElapsedMs
                },
                Warnings = ctx.Warnings
            }
        };

        Log.Information("Snapshot captured: {SnapshotId} | Tx={Tx} | Fields={FieldCount} | Shells={ShellCount} | {Elapsed}ms",
            snapshotId, snapshot.SessionInfo.Transaction, snapshot.Fields.Count, ctx.Shells.Count, budget.ElapsedMs);

        return snapshot;
    }
}
