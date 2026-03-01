using System.Runtime.InteropServices;
using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Provides access to the SAP GUI Scripting Engine via COM interop.
/// No external packages needed — uses native .NET Marshal calls.
/// </summary>
public static class SapEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SapEngine));

    /// <summary>
    /// Gets the SAP GUI Scripting Engine from the running SAP GUI process.
    /// Equivalent to: winax.GetObject("SAPGUI").GetScriptingEngine
    /// </summary>
    public static dynamic GetScriptingEngine()
    {
        try
        {
            Log.Debug("Acquiring SAP GUI COM object...");
            dynamic sapGui = Marshal.GetActiveObject("SAPGUI");
            dynamic engine = sapGui.GetScriptingEngine;
            Log.Debug("SAP GUI Scripting Engine acquired successfully");
            return engine;
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800401E3))
        {
            // MK_E_UNAVAILABLE — SAP GUI not running
            Log.Error("SAP GUI is not running or scripting is disabled");
            throw new Models.SapException(
                Models.SapErrorCodes.SapNotRunning,
                "SAP GUI is not running or scripting is disabled"
            );
        }
        catch (COMException ex)
        {
            Log.Error(ex, "COM error while getting SAP GUI scripting engine");
            throw new Models.SapException(
                Models.SapErrorCodes.ComError,
                $"Failed to get SAP GUI scripting engine: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Gets a specific session by connection and session index.
    /// </summary>
    public static dynamic GetSession(int connectionIndex, int sessionIndex)
    {
        try
        {
            var engine = GetScriptingEngine();
            var conn = engine.Children(connectionIndex);
            var session = conn.Children(sessionIndex);
            Log.Debug("Got session {ConnIdx}/{SesIdx}", connectionIndex, sessionIndex);
            return session;
        }
        catch (Models.SapException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get session {ConnIdx}/{SesIdx}", connectionIndex, sessionIndex);
            throw new Models.SapException(
                Models.SapErrorCodes.SessionNotFound,
                $"Session {connectionIndex}/{sessionIndex} not found: {ex.Message}"
            );
        }
    }
}
