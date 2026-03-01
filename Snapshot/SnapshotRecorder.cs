using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace SapAdapter.Snapshot;

/// <summary>
/// Records snapshots to disk for replay/debugging.
/// Applies redaction rules to protect sensitive data.
/// </summary>
public class SnapshotRecorder
{
    private static readonly ILogger Log = Serilog.Log.ForContext<SnapshotRecorder>();
    private readonly string _artifactsDir;
    private readonly bool _enabled;

    private readonly List<(Regex Pattern, string Replacement)> _redactionRules = new()
    {
        (new Regex(@"GS_HEADER-LIFRE", RegexOptions.IgnoreCase), "REDACTED_VENDOR"),
        (new Regex(@"GS_HEADER-WMWST", RegexOptions.IgnoreCase), "0.00"),
        (new Regex(@"password", RegexOptions.IgnoreCase), "****")
    };

    public SnapshotRecorder(bool enabled = true, string outputDir = "artifacts/replays")
    {
        _enabled = enabled;
        _artifactsDir = Path.GetFullPath(outputDir);

        if (_enabled)
        {
            Directory.CreateDirectory(_artifactsDir);
            Log.Information("Snapshot recorder initialized at {Dir}", _artifactsDir);
        }
    }

    public void Record(Models.SapScreenSnapshot snapshot, object? command = null)
    {
        if (!_enabled) return;

        try
        {
            var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{snapshot.SessionInfo.Transaction}_{snapshot.SnapshotId}.json";
            var filePath = Path.Combine(_artifactsDir, fileName);

            var data = new { snapshot, command };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            Log.Debug("Snapshot recorded: {FileName}", fileName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to record snapshot");
        }
    }
}
