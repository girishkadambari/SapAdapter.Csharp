using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Commands.Handlers;

/// <summary>Tree control command handlers — visible nodes, find, select.</summary>
public static class TreeHandlers
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(TreeHandlers));

    public static async Task<Dictionary<string, object?>> GetVisibleNodes(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        int limit = Convert.ToInt32(payload?["limit"] ?? 50);

        Log.Information("TREE_GET_VISIBLE_NODES for {ShellId} (limit: {Limit})", shellId, limit);

        var tree = SafeCom.Execute(() => session.FindById(shellId), $"find tree {shellId}");
        var allKeys = SafeCom.Execute(() => tree.GetAllNodeKeys(), "get tree keys");
        int count = Math.Min(allKeys.Count, limit);

        var nodes = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            string key = SafeCom.Execute(() => (string)allKeys.Item(idx), "get node key");
            string keyRef = key;
            nodes.Add(new()
            {
                ["key"] = key,
                ["text"] = SafeCom.Execute(() => (string)tree.GetNodeTextByKey(keyRef), "get node text"),
                ["expanded"] = SafeCom.Execute(() => (bool)tree.IsExpandedByKey(keyRef), "is expanded")
            });
        }

        return new() { ["nodes"] = nodes };
    }

    public static async Task<Dictionary<string, object?>> FindNodes(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        var pattern = payload?["pattern"]?.ToString() ?? "";
        int limit = Convert.ToInt32(payload?["limit"] ?? 10);

        Log.Information("TREE_FIND_NODES for {ShellId} (pattern: {Pattern})", shellId, pattern);

        var tree = SafeCom.Execute(() => session.FindById(shellId), $"find tree {shellId}");
        var allKeys = SafeCom.Execute(() => tree.GetAllNodeKeys(), "get tree keys");

        var matches = new List<Dictionary<string, object>>();
        int maxLimit = Math.Min(limit, 50);

        for (int i = 0; i < allKeys.Count && matches.Count < maxLimit; i++)
        {
            int idx = i;
            string key = SafeCom.Execute(() => (string)allKeys.Item(idx), "get node key");
            string keyRef = key;
            string text = SafeCom.Execute(() => (string)tree.GetNodeTextByKey(keyRef), "get text");

            if (text.Contains(pattern))
            {
                matches.Add(new() { ["key"] = key, ["text"] = text });
            }
        }

        return new() { ["matches"] = matches };
    }

    public static async Task<Dictionary<string, object?>> SelectNode(dynamic session, Dictionary<string, object?>? payload)
    {
        var shellId = payload?["shellId"]?.ToString() ?? throw new ArgumentException("shellId required");
        var path = payload?["path"]?.ToString() ?? throw new ArgumentException("path required");

        Log.Information("TREE_SELECT_NODE for {ShellId} (path: {Path})", shellId, path);

        var tree = SafeCom.Execute(() => session.FindById(shellId), $"find tree {shellId}");
        SafeCom.Execute(() => tree.SelectNode(path), $"select node {path}");
        await WaitHelper.WaitForIdle(session);

        return new() { ["success"] = true };
    }
}
