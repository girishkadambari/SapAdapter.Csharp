using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Snapshot.Extractors;

/// <summary>
/// Extracts status bar information (text, type, message ID/number).
/// </summary>
public static class StatusBarExtractor
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(StatusBarExtractor));

    public static Models.SapStatusBar Extract(ExtractorContext ctx)
    {
        ctx.ExtractorsRun.Add("extractStatusBar");
        Log.Debug("Extracting status bar...");

        try
        {
            var bar = SafeCom.Execute(() => ctx.Session.ActiveWindow.StatusBar, "get status bar");
            if (bar == null)
            {
                return new Models.SapStatusBar();
            }

            return new Models.SapStatusBar
            {
                Text = SafeCom.Execute(() => (string)(bar.Text ?? ""), "get StatusBar text"),
                Type = SafeCom.Execute(() => (string)(bar.MessageType ?? ""), "get StatusBar type"),
                MsgId = SafeCom.Execute(() => (string)(bar.MessageId ?? ""), "get msgId"),
                MsgNo = SafeCom.Execute(() => (string)(bar.MessageNumber ?? ""), "get msgNo"),
                Params = new List<string>()
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract status bar, returning empty");
            return new Models.SapStatusBar();
        }
    }
}
