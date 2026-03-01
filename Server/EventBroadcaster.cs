using Fleck;
using Serilog;
using System.Text.Json;

namespace SapAdapter.Server;

/// <summary>
/// Broadcasts events to all connected WebSocket clients.
/// </summary>
public class EventBroadcaster
{
    private static readonly ILogger Log = Serilog.Log.ForContext<EventBroadcaster>();
    private readonly List<IWebSocketConnection> _clients = new();
    private readonly object _lock = new();

    public void AddClient(IWebSocketConnection socket)
    {
        lock (_lock) { _clients.Add(socket); }
    }

    public void RemoveClient(IWebSocketConnection socket)
    {
        lock (_lock) { _clients.Remove(socket); }
    }

    /// <summary>
    /// Broadcast an event to all connected clients.
    /// </summary>
    public void Broadcast(string eventName, object? payload = null)
    {
        var evt = new Models.AdapterEvent
        {
            Type = "event",
            Event = eventName,
            Payload = payload
        };

        var json = JsonSerializer.Serialize(evt);

        List<IWebSocketConnection> snapshot;
        lock (_lock) { snapshot = new List<IWebSocketConnection>(_clients); }

        foreach (var client in snapshot)
        {
            try
            {
                if (client.IsAvailable)
                {
                    client.Send(json);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to broadcast event {Event} to client", eventName);
            }
        }

        Log.Debug("Broadcasted {Event} to {Count} clients", eventName, snapshot.Count);
    }
}
