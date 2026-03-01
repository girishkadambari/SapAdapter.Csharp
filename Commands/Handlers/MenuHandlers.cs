using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Commands.Handlers;

/// <summary>Menu bar command handlers — list paths, select path.</summary>
public static class MenuHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(MenuHandlers));

    public static async Task<Dictionary<string, object?>> GetPaths(dynamic session, Dictionary<string, object?>? payload)
    {
        Log.Information("MENU_GET_PATHS");
        var menu = SafeCom.Execute(() => session.ActiveWindow.MenuBar, "get menu bar");
        var result = new List<string>();
        var items = menu.Children;
        for (int i = 0; i < items.Count; i++)
        {
            int idx = i;
            var topMenu = items.Item(idx);
            result.Add(SafeCom.Execute(() => (string)topMenu.Text, "top menu"));
        }
        return new() { ["paths"] = result };
    }

    public static async Task<Dictionary<string, object?>> SelectPath(dynamic session, Dictionary<string, object?>? payload)
    {
        var path = payload?["path"]?.ToString() ?? throw new ArgumentException("path required");
        Log.Information("MENU_SELECT_PATH: {Path}", path);

        var menu = SafeCom.Execute(() => session.ActiveWindow.MenuBar, "get menu bar");
        SafeCom.Execute(() => menu.Select(path), $"select menu {path}");
        await WaitHelper.WaitForIdle(session);

        return new() { ["success"] = true };
    }
}
