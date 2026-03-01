using SapAdapter.Com;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SapAdapter.Commands.Handlers;

/// <summary>Grid (ALV) command handlers — summary, rows, find.</summary>
public static class GridHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(GridHandlers));

    public static async Task<Dictionary<string, object?>> GetSummary(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        Log.Information("GRID_GET_SUMMARY for {ShellId}", shellId);

        var grid = SafeCom.Execute(() => session.FindById(shellId), $"find grid {shellId}");
        int rowCount = SafeCom.Execute(() => (int)grid.RowCount, "get row count");
        int colCount = SafeCom.Execute(() => (int)grid.ColumnCount, "get col count");
        var columns = SafeCom.Execute(() => grid.ColumnOrder, "get columns");

        var headers = new Dictionary<string, string>();
        for (int i = 0; i < Math.Min(colCount, 30); i++)
        {
            int idx = i;
            string id = SafeCom.Execute(() => (string)columns.Item(idx), "get col id");
            string colRef = id;
            headers[id] = SafeCom.Execute(() => (string)grid.GetColumnHeaderText(colRef), "get header");
        }

        return new() { ["rowCount"] = rowCount, ["colCount"] = colCount, ["headers"] = headers };
    }

    public static async Task<Dictionary<string, object?>> GetRows(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        int startRow = Convert.ToInt32(payload?["startRow"] ?? 0);
        int rowCount = Convert.ToInt32(payload?["rowCount"] ?? 10);

        Log.Information("GRID_GET_ROWS for {ShellId} [{Start}..{End}]", shellId, startRow, startRow + rowCount);

        var grid = SafeCom.Execute(() => session.FindById(shellId), $"find grid {shellId}");

        // Determine columns
        var colOrder = new List<string>();
        if (payload?.ContainsKey("columns") == true && payload["columns"] is JsonElement colArr)
        {
            foreach (var c in colArr.EnumerateArray()) colOrder.Add(c.GetString()!);
        }
        if (colOrder.Count == 0)
        {
            int cnt = SafeCom.Execute(() => (int)grid.ColumnCount, "get col count");
            for (int i = 0; i < Math.Min(cnt, 20); i++)
            {
                int idx = i;
                colOrder.Add(SafeCom.Execute(() => (string)grid.ColumnOrder.Item(idx), "get col"));
            }
        }

        var rows = new List<Dictionary<string, string>>();
        for (int r = startRow; r < Math.Min(startRow + rowCount, startRow + 50); r++)
        {
            var rowData = new Dictionary<string, string>();
            foreach (var col in colOrder)
            {
                int rowRef = r;
                string colRef = col;
                rowData[col] = SafeCom.Execute(() => (string)grid.GetCellValue(rowRef, colRef), $"get cell {r}:{col}");
            }
            rows.Add(rowData);
        }

        return new() { ["rows"] = rows };
    }

    public static async Task<Dictionary<string, object?>> FindRows(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        var column = payload?["column"]?.ToString() ?? throw new ArgumentException("column required");
        var op = payload?["op"]?.ToString() ?? "EQ";
        var value = payload?["value"]?.ToString() ?? "";
        int limit = Convert.ToInt32(payload?["limit"] ?? 10);

        Log.Information("GRID_FIND_ROWS for {ShellId} ({Column} {Op} {Value})", shellId, column, op, value);

        var grid = SafeCom.Execute(() => session.FindById(shellId), $"find grid {shellId}");
        int rowCount = SafeCom.Execute(() => (int)grid.RowCount, "get row count");

        // Resolve column ID (may be header text)
        string colId = column;
        if (!int.TryParse(column, out _))
        {
            int colCount = SafeCom.Execute(() => (int)grid.ColumnCount, "ccnt");
            for (int i = 0; i < colCount; i++)
            {
                int idx = i;
                string id = SafeCom.Execute(() => (string)grid.ColumnOrder.Item(idx), "cid");
                string idRef = id;
                string header = SafeCom.Execute(() => (string)grid.GetColumnHeaderText(idRef), "hdr");
                if (header == column) { colId = id; break; }
            }
        }

        var matches = new List<Dictionary<string, object>>();
        int maxLimit = Math.Min(limit, 50);

        for (int r = 0; r < rowCount && matches.Count < maxLimit; r++)
        {
            int rowRef = r;
            string colRef = colId;
            string cellVal = SafeCom.Execute(() => (string)grid.GetCellValue(rowRef, colRef), $"read {r}:{colId}");

            if (EvaluateQuery(cellVal, op, value))
            {
                matches.Add(new()
                {
                    ["rowIndex"] = r,
                    ["value"] = cellVal,
                    ["matchedCells"] = new Dictionary<string, string> { [colId] = cellVal },
                    ["matchReason"] = $"{op} match on column {colId}"
                });
            }
        }

        return new() { ["matches"] = matches };
    }

    private static bool EvaluateQuery(string cellVal, string op, string target)
    {
        return op switch
        {
            "EQ" => cellVal == target,
            "CONTAINS" => cellVal.Contains(target),
            "REGEX" => Regex.IsMatch(cellVal, target, RegexOptions.IgnoreCase),
            "GT" => TryParseNum(cellVal, out var n1) && TryParseNum(target, out var n2) && n1 > n2,
            "LT" => TryParseNum(cellVal, out var n3) && TryParseNum(target, out var n4) && n3 < n4,
            _ => false
        };
    }

    private static bool TryParseNum(string val, out double result)
    {
        var cleaned = Regex.Replace(val, @"[^\d.,-]", "").Replace(",", ".");
        return double.TryParse(cleaned, out result);
    }
}
