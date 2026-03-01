using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Commands.Handlers;

/// <summary>Basic navigation and field reading handlers (SAFE risk level).</summary>
public static class NavigationHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(NavigationHandlers));

    public static async Task<Dictionary<string, object?>> NavigateTcode(dynamic session, Dictionary<string, object?>? payload)
    {
        var tcode = payload?["tcode"]?.ToString() ?? throw new ArgumentException("tcode required");
        Log.Information("Navigating to transaction: {TCode}", tcode);
        SafeCom.Execute(() => session.StartTransaction(tcode), $"start tx {tcode}");
        await WaitHelper.WaitForIdle(session);
        return new() { ["success"] = true };
    }

    public static async Task<Dictionary<string, object?>> ReadField(dynamic session, Dictionary<string, object?>? payload)
    {
        var id = payload?["id"]?.ToString() ?? throw new ArgumentException("id required");
        Log.Debug("Reading field {FieldId}", id);
        var field = SafeCom.Execute(() => session.FindById(id), $"find field {id}");
        var value = SafeCom.Execute(() => (string)field.Text, "read text");
        return new() { ["value"] = value };
    }

    public static async Task<Dictionary<string, object?>> FocusField(dynamic session, Dictionary<string, object?>? payload)
    {
        var id = payload?["id"]?.ToString() ?? throw new ArgumentException("id required");
        Log.Debug("Focusing field {FieldId}", id);
        var field = SafeCom.Execute(() => session.FindById(id), $"find field {id}");
        SafeCom.Execute(() => field.SetFocus(), "set focus");
        return new() { ["success"] = true };
    }
}
