using Serilog;

namespace SapAdapter.Sessions;

/// <summary>
/// Tracks registered SAP sessions with their connection/session indices.
/// Supports auto-discovery when a session ID is not found.
/// </summary>
public class SessionRegistry
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SessionRegistry));

    private readonly Dictionary<string, RegisteredSession> _sessions = new();

    public record RegisteredSession(
        string SessionId,
        int ConnectionIndex,
        int SessionIndex,
        DateTime LastAccessed
    );

    /// <summary>Register a session by ID with its COM indices.</summary>
    public void Register(string sessionId, int connectionIndex, int sessionIndex)
    {
        _sessions[sessionId] = new RegisteredSession(sessionId, connectionIndex, sessionIndex, DateTime.UtcNow);
        Log.Information("Session registered: {SessionId} → [{ConnIdx},{SesIdx}]", sessionId, connectionIndex, sessionIndex);
    }

    /// <summary>
    /// Gets a registered session. Falls back to auto-discovery if not found.
    /// </summary>
    public RegisteredSession Get(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { LastAccessed = DateTime.UtcNow };
            return session;
        }

        Log.Warning("Session {SessionId} not found in registry, attempting auto-discovery...", sessionId);
        return AutoDiscover(sessionId);
    }

    /// <summary>Checks if any sessions are registered.</summary>
    public bool HasSessions => _sessions.Count > 0;

    private RegisteredSession AutoDiscover(string sessionId)
    {
        try
        {
            var engine = Com.SapEngine.GetScriptingEngine();
            int connCount = Com.SafeCom.Execute(() => engine.Children.Count, "get connection count");
            if (connCount > 0)
            {
                var conn = Com.SafeCom.Execute(() => engine.Children(0), "get connection 0");
                int sesCount = Com.SafeCom.Execute(() => conn.Children.Count, "get session count");
                if (sesCount > 0)
                {
                    Register(sessionId, 0, 0);
                    Log.Information("Auto-discovered session {SessionId} → [0,0]", sessionId);
                    return _sessions[sessionId];
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Auto-discovery failed for session {SessionId}", sessionId);
        }

        throw new Models.SapException(
            Models.SapErrorCodes.SessionNotFound,
            $"No active SAP session mapped to {sessionId}"
        );
    }

    /// <summary>
    /// Lists all active SAP sessions from the running SAP GUI instance.
    /// </summary>
    public List<Models.SessionListItem> ListSessions()
    {
        Log.Information("Listing SAP sessions...");
        var engine = Com.SapEngine.GetScriptingEngine();
        var result = new List<Models.SessionListItem>();

        int connCount = Com.SafeCom.Execute(() => engine.Children.Count, "get connection count");
        Log.Debug("Found {Count} connections", connCount);

        for (int i = 0; i < connCount; i++)
        {
            var conn = Com.SafeCom.Execute(() => engine.Children(i), "get connection " + i);
            int sesCount = Com.SafeCom.Execute(() => conn.Children.Count, "get session count");

            for (int j = 0; j < sesCount; j++)
            {
                try
                {
                    var session = conn.Children(j);
                    var info = session.Info;

                    var item = new Models.SessionListItem
                    {
                        SessionId = $"{i}-{j}",
                        SystemId = Com.SafeCom.Execute(() => (string)info.SystemName, "get SID"),
                        Client = Com.SafeCom.Execute(() => (string)info.Client, "get client"),
                        User = Com.SafeCom.Execute(() => (string)info.User, "get user"),
                        Transaction = Com.SafeCom.Execute(() => (string)info.Transaction, "get tx"),
                        WindowTitle = Com.SafeCom.Execute(() => (string)(session.ActiveWindow?.Text ?? "SAP"), "get title"),
                    };

                    result.Add(item);
                    Log.Debug("Found session: {SessionId} — {User}@{SID} tx:{Tx}",
                        item.SessionId, item.User, item.SystemId, item.Transaction);
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to read session {ConnIdx}-{SesIdx}: {Error}", i, j, ex.Message);
                }
            }
        }

        Log.Information("Found {Count} total sessions", result.Count);
        return result;
    }
}
