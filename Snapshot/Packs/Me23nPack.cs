using Serilog;

namespace SapAdapter.Snapshot.Packs;

/// <summary>
/// ME23N (Display Purchase Order) screen pack.
/// Extracts PO number, vendor, and item overview grid data.
/// </summary>
public class Me23nPack : IScreenPack
{
    private static readonly ILogger Log = Serilog.Log.ForContext<Me23nPack>();

    public bool Match(Models.SapScreenSnapshot snapshot)
    {
        var title = snapshot.Window.Title?.ToUpperInvariant() ?? "";
        return title.Contains("PURCHASE ORDER") || snapshot.SessionInfo.Transaction == "ME23N";
    }

    public void Apply(ExtractorContext ctx, Models.SapScreenSnapshot snapshot)
    {
        Log.Information("Applying Me23nPack to snapshot {SnapshotId}", snapshot.SnapshotId);

        var poFieldId = snapshot.Fields.Keys.FirstOrDefault(id => id.Contains("MEPO_TOPLINE-EBELN"));
        var vendorFieldId = snapshot.Fields.Keys.FirstOrDefault(id => id.Contains("MEPO1222-LIFRE"));

        var entities = snapshot.Entities;

        if (poFieldId != null)
        {
            var val = GetFieldValue(snapshot.Fields[poFieldId]);
            entities = entities with { Po = new Models.SapEntityRef { Id = val, Evidence = new() { poFieldId } } };
        }

        if (vendorFieldId != null)
        {
            var val = GetFieldValue(snapshot.Fields[vendorFieldId]);
            entities = entities with { Vendor = new Models.SapEntityRef { Id = val, Evidence = new() { vendorFieldId } } };
        }

        // Detect Item Overview Shell (GridView)
        var itemOverviewId = snapshot.Fields.Keys.FirstOrDefault(id =>
            id.Contains("SAPLMEGUI") && id.Contains("GRID"));

        if (itemOverviewId != null)
        {
            try
            {
                var shell = Com.SafeCom.Execute(() => ctx.Session.FindById(itemOverviewId), $"find grid {itemOverviewId}");
                if (shell != null)
                {
                    var gridData = Extractors.ShellExtractor.ExtractShell(ctx, shell);
                    // Grid data is already serialized via ShellExtractor
                    Log.Debug("Enriched ME23N item overview for {ShellId}", itemOverviewId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to extract ME23N item overview grid");
            }
        }

        snapshot.Entities = entities;
    }

    private static string GetFieldValue(object field)
    {
        if (field is string s) return s;
        if (field is Models.SapFieldValue fv) return fv.Value?.ToString() ?? "";
        return "";
    }
}
