using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Commands.Handlers;

/// <summary>Field modification handlers (CONFIRM risk level) — setField, pressToolbarButton.</summary>
public static class FieldModificationHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(FieldModificationHandlers));

    public static async Task<Dictionary<string, object?>> SetField(dynamic session, Dictionary<string, object?>? payload)
    {
        var id = payload?["id"]?.ToString() ?? throw new ArgumentException("id required");
        var value = payload?["value"]?.ToString() ?? throw new ArgumentException("value required");

        Log.Information("setField: {FieldId} = {Value}", id, value);
        var field = SafeCom.Execute(() => session.FindById(id), $"find field {id}");
        SafeCom.Execute(() => field.Text = value, $"set text to {value}");
        await WaitHelper.WaitForIdle(session);
        return new() { ["success"] = true };
    }

    public static async Task<Dictionary<string, object?>> PressToolbarButton(dynamic session, Dictionary<string, object?>? payload)
    {
        var id = payload?["id"]?.ToString() ?? throw new ArgumentException("id required");

        Log.Information("pressToolbarButton: {ButtonId}", id);
        var btn = SafeCom.Execute(() => session.FindById(id), $"find btn {id}");
        SafeCom.Execute(() => btn.Press(), "press btn");
        await WaitHelper.WaitForIdle(session);
        return new() { ["success"] = true };
    }
}
