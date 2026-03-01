using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Waits for a SAP session to become idle by polling session.Busy.
/// Also checks the status bar for "Please wait" / "System busy" messages.
/// </summary>
public static class WaitHelper
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(WaitHelper));

    /// <summary>
    /// Blocks until the SAP session is idle or timeout is reached.
    /// </summary>
    public static async Task WaitForIdle(dynamic session, int timeoutMs = 20000)
    {
        var start = DateTime.UtcNow;
        var pollIntervalMs = 500;

        Log.Debug("Waiting for session idle (timeout: {TimeoutMs}ms)...", timeoutMs);

        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            try
            {
                bool isBusy = SafeCom.Execute(() => (bool)session.Busy, "check session.Busy");
                if (!isBusy)
                {
                    // Additional check for "Please wait" in status bar
                    try
                    {
                        var statusBar = session.ActiveWindow?.StatusBar;
                        if (statusBar != null)
                        {
                            string text = SafeCom.Execute(() => (string)(statusBar.Text ?? ""), "read status bar text");
                            if (!text.Contains("Please wait") && !text.Contains("System busy"))
                            {
                                Log.Debug("Session idle after {Elapsed}ms", (DateTime.UtcNow - start).TotalMilliseconds);
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                    catch
                    {
                        // Status bar not available, consider idle
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error checking session busy state");
            }

            await Task.Delay(pollIntervalMs);
        }

        Log.Warning("Session idle wait timed out after {TimeoutMs}ms", timeoutMs);
    }
}
