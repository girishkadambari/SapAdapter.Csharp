using SapAdapter.Com;
using Serilog;
using System.Text.Json;

namespace SapAdapter.Snapshot.Extractors;

/// <summary>
/// Extracts shell metadata (GridView, Table, Tree, Menu) from SAP GUI containers.
/// </summary>
public static class ShellExtractor
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ShellExtractor));

    public static Models.SapFieldValue? ExtractShell(ExtractorContext ctx, dynamic shell)
    {
        string subtype = SafeCom.Execute(() => (string)(shell.SubType ?? "unknown"), "get shell subtype");
        string shellId = SafeCom.Execute(() => (string)shell.Id, "get shell id");

        Log.Debug("Extracting shell {ShellId} (subtype: {SubType})", shellId, subtype);

        var node = new Models.SapFieldValue
        {
            Value = SafeCom.Execute(() => (string)(shell.Text ?? ""), "get shell text"),
            Kind = "unknown",
            Label = $"Shell:{subtype}",
            Visible = true
        };

        object? data = null;
        try
        {
            data = subtype switch
            {
                "GridView" => ExtractGrid(ctx, shell, shellId),
                "Table" => ExtractTable(ctx, shell, shellId),
                "Tree" => ExtractTree(ctx, shell, shellId),
                "Menu" => ExtractMenu(ctx, shell, shellId),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract shell {ShellId}", shellId);
            ctx.Warnings.Add($"Shell extraction failed for {shellId}: {ex.Message}");
        }

        if (data != null)
        {
            return node with { Raw = JsonSerializer.Serialize(data) };
        }

        return node;
    }

    private static object ExtractGrid(ExtractorContext ctx, dynamic shell, string shellId)
    {
        ctx.Shells.Add(new Models.ShellInfo
        {
            ShellId = shellId,
            Kind = "GridView",
            Capabilities = new List<string> { "filtering", "sorting", "paging" },
            SummaryAvailable = true
        });

        int rowCount = SafeCom.Execute(() => (int)shell.RowCount, "get row count");
        int colCount = SafeCom.Execute(() => (int)shell.ColumnCount, "get col count");
        var columns = SafeCom.Execute(() => shell.ColumnOrder, "get columns");

        var headers = new List<string>();
        for (int i = 0; i < Math.Min(colCount, 30); i++)
        {
            headers.Add(SafeCom.Execute(() => (string)columns.Item(i), "get col name"));
        }

        var rows = new List<Dictionary<string, string>>();
        int maxRows = Math.Min(rowCount, 5);

        for (int r = 0; r < maxRows; r++)
        {
            if (!ctx.Budget.IsWithinBudget()) break;
            var rowData = new Dictionary<string, string>();
            foreach (var col in headers)
            {
                if (ctx.Budget.CellReadCount >= ctx.Budget.MaxCellReads)
                {
                    ctx.Budget.MaxCellReadsHit = true;
                    break;
                }
                string colRef = col; // Capture for closure
                rowData[col] = SafeCom.Execute(() => (string)shell.GetCellValue(r, colRef), $"get cell {r}:{col}");
                ctx.Budget.CellReadCount++;
            }
            rows.Add(rowData);
        }

        Log.Debug("Grid {ShellId}: {Rows} rows, {Cols} cols, {Preview} preview rows",
            shellId, rowCount, colCount, rows.Count);

        return new { rowCount, colCount, headers, preview = rows };
    }

    private static object ExtractTable(ExtractorContext ctx, dynamic shell, string shellId)
    {
        ctx.Shells.Add(new Models.ShellInfo
        {
            ShellId = shellId,
            Kind = "Table",
            Capabilities = new List<string> { "scrolling" },
            SummaryAvailable = true
        });

        int rowCount;
        try { rowCount = SafeCom.Execute(() => (int)shell.Rows.Count, "get row count"); }
        catch { rowCount = SafeCom.Execute(() => (int)shell.RowCount, "get row count fallback"); }

        return new { rowCount };
    }

    private static object ExtractTree(ExtractorContext ctx, dynamic shell, string shellId)
    {
        ctx.Shells.Add(new Models.ShellInfo
        {
            ShellId = shellId,
            Kind = "Tree",
            Capabilities = new List<string> { "expansion", "selection" },
            SummaryAvailable = true
        });

        var allKeys = SafeCom.Execute(() => shell.GetAllNodeKeys(), "get tree keys");
        int nodeCount = allKeys?.Count ?? 0;

        return new { nodeCount };
    }

    private static object ExtractMenu(ExtractorContext ctx, dynamic shell, string shellId)
    {
        ctx.Shells.Add(new Models.ShellInfo
        {
            ShellId = shellId,
            Kind = "Menu",
            Capabilities = new List<string> { "navigation" },
            SummaryAvailable = false
        });

        return new { itemPaths = new List<string>() };
    }
}
