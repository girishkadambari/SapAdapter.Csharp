using System.Runtime.InteropServices;
using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Wraps COM calls with structured error handling.
/// Maps cryptic COM exceptions to typed SapException with proper error codes.
/// </summary>
public static class SafeCom
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SafeCom));

    /// <summary>
    /// Executes a COM action safely, catching COM errors and mapping them
    /// to structured SapExceptions.
    /// </summary>
    public static T Execute<T>(Func<T> action, string context = "COM call")
    {
        try
        {
            return action();
        }
        catch (Models.SapException)
        {
            throw; // Already structured, pass through
        }
        catch (COMException ex)
        {
            var msg = ex.Message;

            // RPC_E_CALL_REJECTED — SAP busy
            if (msg.Contains("0x80010105") || ex.HResult == unchecked((int)0x80010105))
            {
                Log.Warning("SAP busy during {Context}: {Message}", context, msg);
                throw new Models.SapException(
                    Models.SapErrorCodes.Busy,
                    $"SAP is busy or not responding: {context}"
                );
            }

            // Disconnected object
            if (msg.Contains("The object invoked has disconnected"))
            {
                Log.Error("SAP GUI object disconnected during {Context}", context);
                throw new Models.SapException(
                    Models.SapErrorCodes.ComError,
                    "SAP GUI object disconnected"
                );
            }

            Log.Error(ex, "COM error in {Context}", context);
            throw new Models.SapException(
                Models.SapErrorCodes.ComError,
                $"COM error in {context}: {msg}"
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in {Context}", context);
            throw new Models.SapException(
                Models.SapErrorCodes.ComError,
                $"Error in {context}: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Executes a void COM action safely.
    /// </summary>
    public static void Execute(Action action, string context = "COM call")
    {
        Execute(() => { action(); return true; }, context);
    }
}
