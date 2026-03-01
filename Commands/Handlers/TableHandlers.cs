using SapAdapter.Com;
using Serilog;
using System.Text.RegularExpressions;

namespace SapAdapter.Commands.Handlers;

/// <summary>Table control command handlers — summary, rows, find.</summary>
public static class TableHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(TableHandlers));

    public static async Task<Dictionary<string, object?>> GetSummary(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        Log.Information("TABLE_GET_SUMMARY for {ShellId}", shellId);

        var table = SafeCom.Execute(() => session.FindById(shellId), $"find table {shellId}");
        int rowCount = SafeCom.Execute(() => (int)table.RowCount, "get rcnt");
        int colCount = SafeCom.Execute(() => (int)table.Columns.Count, "get ccnt");

        var headers = new List<string>();
        for (int i = 0; i < Math.Min(colCount, 20); i++)
        {
            int idx = i;
            headers.Add(SafeCom.Execute(() => (string)table.Columns.Item(idx).Name, "get name"));
        }

        return new() { ["rowCount"] = rowCount, ["colCount"] = colCount, ["headers"] = headers };
    }

    public static async Task<Dictionary<string, object?>> GetRows(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        int startRow = Convert.ToInt32(payload?["startRow"] ?? 0);
        int rowCount = Convert.ToInt32(payload?["rowCount"] ?? 10);

        Log.Information("TABLE_GET_ROWS for {ShellId} [{Start}..{End}]", shellId, startRow, startRow + rowCount);

        var table = SafeCom.Execute(() => session.FindById(shellId), $"find table {shellId}");
        int colCount = SafeCom.Execute(() => (int)table.Columns.Count, "ccnt");

        var rows = new List<Dictionary<string, string>>();
        for (int r = startRow; r < Math.Min(startRow + rowCount, startRow + 50); r++)
        {
            var rowData = new Dictionary<string, string>();
            for (int c = 0; c < Math.Min(colCount, 20); c++)
            {
                int rowRef = r, colRef = c;
                var cell = SafeCom.Execute(() => table.GetCell(rowRef, colRef), $"cell {r}:{c}");
                string name = SafeCom.Execute(() => (string)table.Columns.Item(colRef).Name, "name");
                rowData[name] = SafeCom.Execute(() => (string)cell.Text, "txt");
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

        Log.Information("TABLE_FIND_ROWS for {ShellId} ({Column} {Op} {Value})", shellId, column, op, value);

        var table = SafeCom.Execute(() => session.FindById(shellId), $"find table {shellId}");
        int rowCount = SafeCom.Execute(() => (int)table.RowCount, "rcnt");

        var matches = new List<Dictionary<string, object>>();
        int maxLimit = Math.Min(limit, 50);

        for (int r = 0; r < rowCount && matches.Count < maxLimit; r++)
        {
            int rowRef = r;
            var cell = SafeCom.Execute(() =>
            {
                if (int.TryParse(column, out var colIdx))
                    return table.GetCell(rowRef, colIdx);
                return table.GetCell(rowRef, column);
            }, $"get table cell {r}:{column}");

            string cellVal = SafeCom.Execute(() => (string)cell.Text, "txt");

            if (EvaluateQuery(cellVal, op, value))
            {
                matches.Add(new() { ["rowIndex"] = r, ["value"] = cellVal });
            }
        }

        return new() { ["matches"] = matches };
    }

    private static bool EvaluateQuery(string cellVal, string op, string target) =>
        op switch
        {
            "EQ" => cellVal == target,
            "CONTAINS" => cellVal.Contains(target),
            "REGEX" => Regex.IsMatch(cellVal, target, RegexOptions.IgnoreCase),
            "GT" => double.TryParse(Regex.Replace(cellVal, @"[^\d.,-]", "").Replace(",", "."), out var n1) &&
                    double.TryParse(target, out var n2) && n1 > n2,
            "LT" => double.TryParse(Regex.Replace(cellVal, @"[^\d.,-]", "").Replace(",", "."), out var n3) &&
                    double.TryParse(target, out var n4) && n3 < n4,
            _ => false
        };
}
