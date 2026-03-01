using System.Runtime.InteropServices;
using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Wraps COM calls with structured error handling and apartment marshalling.
/// Bridges the gap between MTA WebSocket threads and SAP's STA requirement.
/// </summary>
public static class SafeCom
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SafeCom));

    /// <summary>
    /// Executes a COM action safely. 
    /// Automatically marshals the call to an STA thread if the current thread is MTA.
    /// </summary>
    public static T Execute<T>(Func<T> action, string context = "COM call")
    {
        // If we are already in STA, execute directly
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return ExecuteInternal(action, context);
        }

        // Otherwise, marshal to a new STA thread (or a pool in a larger app)
        Log.Debug("Marshalling {Context} to STA thread...", context);
        T result = default!;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = ExecuteInternal(action, context);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null) throw error;
        return result;
    }

    /// <summary>
    /// Core execution logic with exception mapping.
    /// </summary>
    private static T ExecuteInternal<T>(Func<T> action, string context)
    {
        try
        {
            return action();
        }
        catch (Models.SapException)
        {
            throw; // Pass through
        }
        catch (COMException ex)
        {
            Log.Debug("COM Error (HRESULT: 0x{HR:X8}) in {Context}: {Msg}", ex.HResult, context, ex.Message);
            
            // RPC_E_CALL_REJECTED (0x80010105)
            if (ex.HResult == unchecked((int)0x80010105))
            {
                throw new Models.SapException(Models.SapErrorCodes.Busy, "SAP is busy or not responding");
            }
            // CO_E_CLASSSTRING (0x800401F3)
            if (ex.HResult == unchecked((int)0x800401F3))
            {
                throw new Models.SapException(Models.SapErrorCodes.SapNotRunning, "SAP GUI COM class not registered");
            }

            throw new Models.SapException(Models.SapErrorCodes.ComError, $"COM error in {context}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in {Context}", context);
            throw new Models.SapException(Models.SapErrorCodes.ComError, $"Error in {context}: {ex.Message}");
        }
    }

    public static void Execute(Action action, string context = "COM call")
    {
        Execute(() => { action(); return true; }, context);
    }
}
