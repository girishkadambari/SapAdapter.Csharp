using System.Text.RegularExpressions;
using Serilog;

namespace SapAdapter.Snapshot.Packs;

/// <summary>
/// MIRO (Enter Incoming Invoice) screen pack.
/// Extracts vendor, invoice amount/currency, and PO from MIRO fields.
/// </summary>
public class MiroPack : IScreenPack
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MiroPack>();

    public bool Match(Models.SapScreenSnapshot snapshot)
    {
        return snapshot.SessionInfo.Transaction == "MIRO"
            || (snapshot.Window.Title?.Contains("Enter Incoming Invoice") ?? false);
    }

    public void Apply(ExtractorContext ctx, Models.SapScreenSnapshot snapshot)
    {
        Log.Information("Applying MiroPack to snapshot {SnapshotId}", snapshot.SnapshotId);

        var vendorId = FindFieldByPattern(snapshot.Fields, new Regex(@"LIFRE|VENDOR", RegexOptions.IgnoreCase));
        var amountId = FindFieldByPattern(snapshot.Fields, new Regex(@"WMWST|RM08M-WERTB|TOTAL_AMOUNT", RegexOptions.IgnoreCase));
        var currencyId = FindFieldByPattern(snapshot.Fields, new Regex(@"WAERS|CURRENCY", RegexOptions.IgnoreCase));
        var poId = FindFieldByPattern(snapshot.Fields, new Regex(@"EBELN|PO_NUMBER", RegexOptions.IgnoreCase));

        var entities = snapshot.Entities;

        if (vendorId != null)
        {
            entities = entities with { Vendor = new Models.SapEntityRef { Id = GetFieldValue(snapshot.Fields[vendorId]), Evidence = new() { vendorId } } };
        }

        if (amountId != null || currencyId != null)
        {
            entities = entities with
            {
                Invoice = new Models.SapInvoiceEntity
                {
                    Id = snapshot.SnapshotId,
                    Amount = amountId != null ? ParseSapNumber(GetFieldValue(snapshot.Fields[amountId])) : null,
                    Currency = currencyId != null ? GetFieldValue(snapshot.Fields[currencyId]) : null,
                    Evidence = new[] { amountId, currencyId }.Where(x => x != null).Cast<string>().ToList()
                }
            };
        }

        if (poId != null)
        {
            entities = entities with { Po = new Models.SapEntityRef { Id = GetFieldValue(snapshot.Fields[poId]), Evidence = new() { poId } } };
        }

        // Fallback heuristics for PO
        if (entities.Po == null)
        {
            foreach (var (id, field) in snapshot.Fields)
            {
                var val = GetFieldValue(field);
                if (Regex.IsMatch(val, @"^\d{10}$") && (id.Contains("EBELN") || id.Contains("PO")))
                {
                    entities = entities with { Po = new Models.SapEntityRef { Id = val, Evidence = new() { id } } };
                    break;
                }
            }
        }

        // Update snapshot entities (record with-expression via reflection since snapshot is mutable via init)
        // We need to return the updated snapshot, so use a mutable approach
        snapshot.Entities = entities;
    }

    private static string? FindFieldByPattern(Dictionary<string, object> fields, Regex pattern)
        => fields.Keys.FirstOrDefault(id => pattern.IsMatch(id));

    private static string GetFieldValue(object field)
    {
        if (field is string s) return s;
        if (field is Models.SapFieldValue fv) return fv.Value?.ToString() ?? "";
        return "";
    }

    private static double ParseSapNumber(string val)
    {
        var cleaned = Regex.Replace(val, @"[^\d.,-]", "").Replace(",", ".");
        return double.TryParse(cleaned, out var result) ? result : 0;
    }
}
