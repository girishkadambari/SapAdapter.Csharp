using SapAdapter.Commands.Handlers;
using Serilog;
using System.Text.Json;

namespace SapAdapter.Commands;

/// <summary>
/// Routes command type strings to their handler functions.
/// Handles idempotency checking and modal detection before execution.
/// </summary>
public class CommandRouter
{
    private static readonly ILogger Log = Serilog.Log.ForContext<CommandRouter>();

    private readonly IdempotencyCache _idempotency;
    private readonly Sessions.SessionRegistry _sessions;

    private delegate Task<Dictionary<string, object?>> HandlerFunc(dynamic session, Dictionary<string, object?>? payload);

    private readonly Dictionary<string, HandlerFunc> _safeHandlers;
    private readonly Dictionary<string, HandlerFunc> _confirmHandlers;

    public CommandRouter(Sessions.SessionRegistry sessions, IdempotencyCache idempotency)
    {
        _sessions = sessions;
        _idempotency = idempotency;

        _safeHandlers = new()
        {
            // Navigation
            ["navigateTcode"] = NavigationHandlers.NavigateTcode,
            ["readField"] = NavigationHandlers.ReadField,
            ["focusField"] = NavigationHandlers.FocusField,

            // Grid
            ["GRID_GET_SUMMARY"] = GridHandlers.GetSummary,
            ["GRID_GET_ROWS"] = GridHandlers.GetRows,
            ["GRID_FIND_ROWS"] = GridHandlers.FindRows,

            // Table
            ["TABLE_GET_SUMMARY"] = TableHandlers.GetSummary,
            ["TABLE_GET_ROWS"] = TableHandlers.GetRows,
            ["TABLE_FIND_ROWS"] = TableHandlers.FindRows,

            // Tree
            ["TREE_GET_VISIBLE_NODES"] = TreeHandlers.GetVisibleNodes,
            ["TREE_FIND_NODES"] = TreeHandlers.FindNodes,
            ["TREE_SELECT_NODE"] = TreeHandlers.SelectNode,

            // Menu
            ["MENU_GET_PATHS"] = MenuHandlers.GetPaths,
            ["MENU_SELECT_PATH"] = MenuHandlers.SelectPath,

            // OTC Workflows
            ["GET_SALES_ORDER"] = OtcHandlers.GetSalesOrder,
            ["CHECK_CREDIT_LIMIT"] = OtcHandlers.CheckCreditLimit,
            ["GET_RECEIVABLES"] = OtcHandlers.GetReceivables,
            ["NAVIGATE_CREDIT_RELEASE"] = OtcHandlers.NavigateCreditRelease,
            ["RELEASE_CREDIT_BLOCK"] = OtcHandlers.ReleaseCreditBlock,
            ["OPEN_PO_DISPLAY"] = OtcHandlers.OpenPoDisplay,
            ["OPEN_PO_HISTORY"] = OtcHandlers.OpenPoHistory,
        };

        _confirmHandlers = new()
        {
            ["setField"] = FieldModificationHandlers.SetField,
            ["pressToolbarButton"] = FieldModificationHandlers.PressToolbarButton,
        };
    }

    /// <summary>
    /// Executes a command by type, resolving the session and applying
    /// idempotency + modal checks.
    /// </summary>
    public async Task<object> Execute(string sessionId, string type, Dictionary<string, object?>? payload, string? idempotencyKey)
    {
        Log.Information("Executing command {Type} for session {SessionId}", type, sessionId);

        // Idempotency check
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var cached = _idempotency.Get(idempotencyKey);
            if (cached != null)
            {
                Log.Information("Idempotent result returned for key {Key}", idempotencyKey);
                return cached;
            }
        }

        // Resolve session
        var reg = _sessions.Get(sessionId);
        var session = Com.SapEngine.GetSession(reg.ConnectionIndex, reg.SessionIndex);

        // Modal check
        ModalChecker.Check(session);

        // Find handler
        HandlerFunc? handler = null;
        if (_safeHandlers.TryGetValue(type, out handler) || _confirmHandlers.TryGetValue(type, out handler))
        {
            var result = await handler(session, payload);

            // Cache result
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _idempotency.Set(idempotencyKey, result);
            }

            Log.Information("Command {Type} completed successfully", type);
            return result;
        }

        throw new Models.SapException(Models.SapErrorCodes.CommandFailed, $"Unknown command type: {type}");
    }
}
