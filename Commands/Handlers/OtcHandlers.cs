using SapAdapter.Com;
using Serilog;
using System.Text.RegularExpressions;

namespace SapAdapter.Commands.Handlers;

/// <summary>
/// OTC (Order-to-Cash) domain-specific handlers.
/// GET_SALES_ORDER, CHECK_CREDIT_LIMIT, GET_RECEIVABLES, NAVIGATE_CREDIT_RELEASE, RELEASE_CREDIT_BLOCK, etc.
/// </summary>
public static class OtcHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(OtcHandlers));

    public static async Task<Dictionary<string, object?>> GetSalesOrder(dynamic session, Dictionary<string, object?>? payload)
    {
        var salesOrder = payload?["salesOrder"]?.ToString() ?? throw new ArgumentException("salesOrder required");
        Log.Information("GET_SALES_ORDER: {SO}", salesOrder);

        SafeCom.Execute(() => session.StartTransaction("VA03"), "start VA03");
        await WaitHelper.WaitForIdle(session);

        var orderField = SafeCom.Execute(() => session.FindById("wnd[0]/usr/ctxtVBAK-VBELN"), "find VBAK-VBELN");
        SafeCom.Execute(() => orderField.Text = salesOrder, "set SO");
        SafeCom.Execute(() => session.ActiveWindow.SendVKey(0), "press Enter");
        await WaitHelper.WaitForIdle(session);

        string netValue = SafeCom.Execute(() => (string)session.FindById("wnd[0]/usr/subSUBSCREEN_HEADER:SAPMV45A:4021/txtVBAK-NETWR").Text, "read net val");
        string deliveryBlock = SafeCom.Execute(() => (string)session.FindById("wnd[0]/usr/subSUBSCREEN_HEADER:SAPMV45A:4021/ctxtVBAK-LIFSK").Text, "read block");
        string status = SafeCom.Execute(() => (string)session.FindById("wnd[0]/sbar").Text, "read sbar");

        return new()
        {
            ["success"] = true,
            ["data"] = new Dictionary<string, object?>
            {
                ["salesOrder"] = salesOrder,
                ["status"] = string.IsNullOrEmpty(status) ? "Loaded" : status,
                ["netValue"] = netValue.Trim(),
                ["deliveryBlock"] = deliveryBlock.Trim()
            }
        };
    }

    public static async Task<Dictionary<string, object?>> CheckCreditLimit(dynamic session, Dictionary<string, object?>? payload)
    {
        var customer = payload?["customer"]?.ToString() ?? throw new ArgumentException("customer required");
        Log.Information("CHECK_CREDIT_LIMIT for customer: {Customer}", customer);

        SafeCom.Execute(() => session.StartTransaction("FD32"), "start FD32");
        await WaitHelper.WaitForIdle(session);
        SafeCom.Execute(() => session.FindById("wnd[0]/usr/ctxtRF02L-KUNNR").Text = customer, "set customer");
        SafeCom.Execute(() => session.FindById("wnd[0]/usr/chkRF02L-D0110").Selected = true, "check status");
        SafeCom.Execute(() => session.ActiveWindow.SendVKey(0), "press Enter");
        await WaitHelper.WaitForIdle(session);

        string limit = SafeCom.Execute(() => (string)session.FindById("wnd[0]/usr/txtKNKK-KLTOL").Text, "read limit");
        string exposure = SafeCom.Execute(() => (string)session.FindById("wnd[0]/usr/txtRF02L-SAKNR").Text, "read exposure");
        string risk = SafeCom.Execute(() => (string)session.FindById("wnd[0]/usr/ctxtKNKK-CTLPC").Text, "read risk");

        return new()
        {
            ["success"] = true,
            ["data"] = new Dictionary<string, object?>
            {
                ["creditLimit"] = ParseSapNumber(limit),
                ["currentExposure"] = ParseSapNumber(exposure),
                ["riskCategory"] = risk
            }
        };
    }

    public static async Task<Dictionary<string, object?>> GetReceivables(dynamic session, Dictionary<string, object?>? payload)
    {
        var customer = payload?["customer"]?.ToString() ?? throw new ArgumentException("customer required");
        Log.Information("GET_RECEIVABLES for customer: {Customer}", customer);

        SafeCom.Execute(() => session.StartTransaction("FBL5N"), "start FBL5N");
        await WaitHelper.WaitForIdle(session);
        SafeCom.Execute(() => session.FindById("wnd[0]/usr/ctxtDD_KUNNR-LOW").Text = customer, "set customer");
        SafeCom.Execute(() => session.FindById("wnd[0]/usr/radX_OPENT").Select(), "select open items");
        SafeCom.Execute(() => session.ActiveWindow.SendVKey(8), "Execute (F8)");
        await WaitHelper.WaitForIdle(session);

        var grid = SafeCom.Execute(() => session.FindById("wnd[0]/usr/cntlGRID1/shellcont/shell"), "find grid");
        int rowCount = SafeCom.Execute(() => (int)grid.RowCount, "row count");

        var items = new List<Dictionary<string, string>>();
        for (int i = 0; i < Math.Min(rowCount, 10); i++)
        {
            int idx = i;
            items.Add(new()
            {
                ["amount"] = SafeCom.Execute(() => (string)grid.GetCellValue(idx, "WRBTR"), "amt"),
                ["dueDate"] = SafeCom.Execute(() => (string)grid.GetCellValue(idx, "FAEDT"), "due"),
                ["document"] = SafeCom.Execute(() => (string)grid.GetCellValue(idx, "BELNR"), "doc")
            });
        }

        return new() { ["success"] = true, ["data"] = new Dictionary<string, object?> { ["items"] = items } };
    }

    public static async Task<Dictionary<string, object?>> NavigateCreditRelease(dynamic session, Dictionary<string, object?>? payload)
    {
        var salesOrder = payload?["salesOrder"]?.ToString() ?? throw new ArgumentException("salesOrder required");
        Log.Information("NAVIGATE_CREDIT_RELEASE for SO: {SO}", salesOrder);

        SafeCom.Execute(() => session.StartTransaction("VKM3"), "start VKM3");
        await WaitHelper.WaitForIdle(session);
        SafeCom.Execute(() => session.ActiveWindow.SendVKey(8), "Execute (F8)");
        await WaitHelper.WaitForIdle(session);

        var grid = SafeCom.Execute(() => session.FindById("wnd[0]/usr/cntlGRID1/shellcont/shell"), "find VKM3 grid");
        int rowCount = SafeCom.Execute(() => (int)grid.RowCount, "rcnt");
        int foundRow = -1;

        for (int i = 0; i < rowCount; i++)
        {
            int idx = i;
            string vbeln = SafeCom.Execute(() => (string)grid.GetCellValue(idx, "VBELN"), "read VBELN");
            if (vbeln == salesOrder)
            {
                foundRow = i;
                break;
            }
        }

        if (foundRow == -1)
        {
            return new() { ["success"] = false, ["error"] = $"Sales order {salesOrder} not found in VKM3" };
        }

        int fr = foundRow;
        SafeCom.Execute(() => grid.SelectedRows = fr.ToString(), "select row");
        return new() { ["success"] = true, ["target"] = "VKM3", ["salesOrder"] = salesOrder, ["rowIndex"] = foundRow };
    }

    public static async Task<Dictionary<string, object?>> ReleaseCreditBlock(dynamic session, Dictionary<string, object?>? payload)
    {
        Log.Information("RELEASE_CREDIT_BLOCK");
        SafeCom.Execute(() => session.FindById("wnd[0]/tbar[1]/btn[24]").Press(), "press Release");
        await WaitHelper.WaitForIdle(session);
        SafeCom.Execute(() => session.ActiveWindow.SendVKey(11), "Save");
        await WaitHelper.WaitForIdle(session);
        return new() { ["success"] = true };
    }

    public static async Task<Dictionary<string, object?>> OpenPoDisplay(dynamic session, Dictionary<string, object?>? payload)
    {
        var poId = payload?["poId"]?.ToString() ?? "";
        Log.Information("OPEN_PO_DISPLAY: {PoId}", poId);
        SafeCom.Execute(() => session.StartTransaction("ME23N"), "start ME23N");
        await WaitHelper.WaitForIdle(session);
        return new() { ["success"] = true, ["target"] = "ME23N", ["poId"] = poId };
    }

    public static async Task<Dictionary<string, object?>> OpenPoHistory(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        int rowIndex = Convert.ToInt32(payload?["rowIndex"] ?? 0);
        Log.Information("OPEN_PO_HISTORY: {ShellId} row {Row}", shellId, rowIndex);

        var grid = SafeCom.Execute(() => session.FindById(shellId), "find grid");
        SafeCom.Execute(() => grid.SetCurrentCell(rowIndex, "EBELP"), "focus item");
        return new() { ["success"] = true, ["action"] = "PO_HISTORY_TRIGGERED" };
    }

    private static double ParseSapNumber(string val)
    {
        var cleaned = Regex.Replace(val, @"[^\d.,-]", "").Replace(",", ".");
        return double.TryParse(cleaned, out var result) ? result : 0;
    }
}
