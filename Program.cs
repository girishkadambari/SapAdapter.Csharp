using Microsoft.Extensions.Configuration;
using Serilog;

namespace SapAdapter;

/// <summary>
/// SAP Copilot Adapter — C# Edition
/// 
/// Drop-in replacement for the TypeScript adapter.
/// Uses native .NET COM interop with SAP GUI — no winax, Python, or node-gyp needed.
/// 
/// Usage:
///   dotnet run                           # Uses default port 8787
///   dotnet run -- --port 9090            # Custom port
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // ── Configuration ──────────────────────────────────────────────
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        // ── Logging ────────────────────────────────────────────────────
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                config["Serilog:WriteTo:1:Args:path"] ?? "logs/adapter-.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("╔══════════════════════════════════════════╗");
            Log.Information("║   SAP Copilot Adapter (C# Edition)      ║");
            Log.Information("╚══════════════════════════════════════════╝");

            // ── Parse Config ───────────────────────────────────────────
            int port = config.GetValue("Adapter:Port", 8787);
            int waitTimeoutMs = config.GetValue("Sap:WaitTimeoutMs", 20000);
            int maxCellReads = config.GetValue("Sap:MaxCellReads", 500);
            int maxNodes = config.GetValue("Sap:MaxNodes", 5000);
            int maxSnapshotMs = config.GetValue("Sap:MaxSnapshotDurationMs", 4000);
            bool recorderEnabled = config.GetValue("Recorder:Enabled", true);
            string recorderDir = config["Recorder:OutputDir"] ?? "artifacts/replays";
            int idempMaxEntries = config.GetValue("Idempotency:MaxEntries", 500);
            int idempTtlSec = config.GetValue("Idempotency:TtlSeconds", 300);

            // Override port from command line
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port" && int.TryParse(args[i + 1], out var cliPort))
                    port = cliPort;
            }

            Log.Information("Configuration loaded:");
            Log.Information("  Port: {Port}", port);
            Log.Information("  Wait Timeout: {Timeout}ms", waitTimeoutMs);
            Log.Information("  Max Cell Reads: {MaxCells}", maxCellReads);
            Log.Information("  Recorder: {Enabled} → {Dir}", recorderEnabled, recorderDir);

            // ── Build Components ───────────────────────────────────────
            var sessions = new Sessions.SessionRegistry();
            var idempotency = new Commands.IdempotencyCache(idempMaxEntries, idempTtlSec);
            var commands = new Commands.CommandRouter(sessions, idempotency);
            var snapshots = new Snapshot.SnapshotPipeline(maxSnapshotMs, maxNodes, maxCellReads);
            var recorder = new Snapshot.SnapshotRecorder(recorderEnabled, recorderDir);
            var events = new Server.EventBroadcaster();

            var server = new Server.WebSocketServer(port, sessions, commands, snapshots, recorder, events);

            // ── Start ──────────────────────────────────────────────────
            server.Start();
            Log.Information("Using provider: real (native COM)");

            Log.Information("Press Ctrl+C to stop...");
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; exitEvent.Set(); };
            exitEvent.WaitOne();

            Log.Information("Shutting down...");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Adapter failed to start");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
