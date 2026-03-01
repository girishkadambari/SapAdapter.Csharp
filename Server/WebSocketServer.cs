using Fleck;
using SapAdapter.Commands;
using SapAdapter.Sessions;
using SapAdapter.Snapshot;
using Serilog;
using System.Text.Json;

namespace SapAdapter.Server;

/// <summary>
/// Main WebSocket server that handles adapter requests.
/// Drop-in replacement for the TypeScript WebSocket server — same JSON protocol.
/// </summary>
public class WebSocketServer
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WebSocketServer>();

    private readonly int _port;
    private readonly SessionRegistry _sessions;
    private readonly CommandRouter _commands;
    private readonly SnapshotPipeline _snapshots;
    private readonly SnapshotRecorder _recorder;
    private readonly EventBroadcaster _events;
    private Fleck.WebSocketServer? _server;

    public WebSocketServer(
        int port,
        SessionRegistry sessions,
        CommandRouter commands,
        SnapshotPipeline snapshots,
        SnapshotRecorder recorder,
        EventBroadcaster events)
    {
        _port = port;
        _sessions = sessions;
        _commands = commands;
        _snapshots = snapshots;
        _recorder = recorder;
        _events = events;
    }

    public void Start()
    {
        _server = new Fleck.WebSocketServer($"ws://0.0.0.0:{_port}");

        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                Log.Information("Client connected from {Remote}", socket.ConnectionInfo.ClientIpAddress);
                _events.AddClient(socket);
            };

            socket.OnClose = () =>
            {
                Log.Information("Client disconnected: {Remote}", socket.ConnectionInfo.ClientIpAddress);
                _events.RemoveClient(socket);
            };

            socket.OnMessage = async message =>
            {
                await HandleMessage(socket, message);
            };

            socket.OnError = ex =>
            {
                Log.Error(ex, "WebSocket error for client {Remote}", socket.ConnectionInfo.ClientIpAddress);
            };
        });

        Log.Information("SAP Adapter started on ws://localhost:{Port}", _port);
    }

    private async Task HandleMessage(IWebSocketConnection socket, string message)
    {
        Models.AdapterRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<Models.AdapterRequest>(message);
            if (request == null) throw new Exception("Failed to parse request");

            Log.Information("Request received: {Type} ({Id})", request.Type, request.Id);

            object? result = request.Type switch
            {
                "healthCheck" => HandleHealthCheck(),
                "listSessions" => HandleListSessions(),
                "attachSession" => HandleAttachSession(request.Payload),
                "captureSnapshot" => HandleCaptureSnapshot(request.Payload),
                "executeCommand" => await HandleExecuteCommand(request.Payload),
                _ => throw new Models.SapException(Models.SapErrorCodes.UnknownRequest, $"Unknown request type: {request.Type}")
            };

            var response = new Models.AdapterResponse
            {
                Id = request.Id,
                Ok = true,
                Payload = result
            };

            socket.Send(JsonSerializer.Serialize(response));
            Log.Debug("Response sent for {Type} ({Id})", request.Type, request.Id);
        }
        catch (Models.SapException ex)
        {
            Log.Warning(ex, "SAP error processing {Type}", request?.Type ?? "unknown");
            SendError(socket, request?.Id ?? "", ex.Code, ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error processing message");
            SendError(socket, request?.Id ?? "", Models.SapErrorCodes.ServerError, ex.Message);
        }
    }

    private object HandleHealthCheck()
    {
        Log.Information("Health check OK");
        return new { status = "OK", timestamp = DateTime.UtcNow.ToString("o") };
    }

    private object HandleListSessions()
    {
        try
        {
            return _sessions.ListSessions();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ListSessions failed (SAP GUI may not be running), returning empty list");
            return Array.Empty<object>();
        }
    }

    private object HandleAttachSession(Dictionary<string, object?>? payload)
    {
        var sessionId = GetPayloadString(payload, "sessionId");
        Log.Information("Attaching session: {SessionId}", sessionId);

        // Parse session ID format "connIdx-sesIdx"
        var parts = sessionId.Split('-');
        int connIdx = parts.Length > 0 ? int.Parse(parts[0]) : 0;
        int sesIdx = parts.Length > 1 ? int.Parse(parts[1]) : 0;

        _sessions.Register(sessionId, connIdx, sesIdx);
        return new { success = true, sessionId };
    }

    private object HandleCaptureSnapshot(Dictionary<string, object?>? payload)
    {
        var sessionId = GetPayloadString(payload, "sessionId");
        Log.Information("Capturing snapshot for session: {SessionId}", sessionId);

        var reg = _sessions.Get(sessionId);
        var snapshot = _snapshots.Capture(sessionId, reg.ConnectionIndex, reg.SessionIndex);

        _recorder.Record(snapshot);
        _events.Broadcast("snapshot.created", new { snapshotId = snapshot.SnapshotId, sessionId });

        return snapshot;
    }

    private async Task<object> HandleExecuteCommand(Dictionary<string, object?>? payload)
    {
        var sessionId = GetPayloadString(payload, "sessionId");
        var type = GetPayloadString(payload, "type");
        var idempotencyKey = payload?.ContainsKey("idempotencyKey") == true ? payload["idempotencyKey"]?.ToString() : null;

        // Extract nested command payload
        Dictionary<string, object?>? cmdPayload = null;
        if (payload?.ContainsKey("payload") == true && payload["payload"] is JsonElement je)
        {
            cmdPayload = JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
        }

        _events.Broadcast("command.started", new { type, sessionId });

        try
        {
            var result = await _commands.Execute(sessionId, type, cmdPayload, idempotencyKey);

            // Capture post-command snapshot
            try
            {
                var reg = _sessions.Get(sessionId);
                var snap = _snapshots.Capture(sessionId, reg.ConnectionIndex, reg.SessionIndex);
                _recorder.Record(snap, new { type, payload = cmdPayload, result });
                _events.Broadcast("snapshot.created", new { snapshotId = snap.SnapshotId, sessionId });
            }
            catch (Exception snapErr)
            {
                Log.Warning(snapErr, "Post-command snapshot capture failed");
            }

            _events.Broadcast("command.finished", new { type, sessionId, success = true });
            return result;
        }
        catch
        {
            _events.Broadcast("command.finished", new { type, sessionId, success = false });
            throw;
        }
    }

    private void SendError(IWebSocketConnection socket, string id, string code, string message, object? details = null)
    {
        var response = new Models.AdapterResponse
        {
            Id = id,
            Ok = false,
            Error = new Models.AdapterError
            {
                Code = code,
                Message = message,
                Details = details
            }
        };

        try
        {
            socket.Send(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send error response");
        }
    }

    private static string GetPayloadString(Dictionary<string, object?>? payload, string key)
    {
        if (payload == null || !payload.ContainsKey(key) || payload[key] == null)
            throw new ArgumentException($"{key} is required in payload");

        if (payload[key] is JsonElement je)
            return je.GetString() ?? throw new ArgumentException($"{key} is null");

        return payload[key]!.ToString()!;
    }
}
