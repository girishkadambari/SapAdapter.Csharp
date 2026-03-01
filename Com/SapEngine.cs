using System.Runtime.InteropServices;
using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Provides access to the SAP GUI Scripting Engine via COM interop.
/// Uses P/Invoke to oleaut32.dll since Marshal.GetActiveObject was removed in .NET 5+.
/// </summary>
public static class SapEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SapEngine));

    // P/Invoke declarations for COM activation (replaces Marshal.GetActiveObject)
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid lpclsid);

    /// <summary>
    /// .NET 8 replacement for Marshal.GetActiveObject(progId).
    /// Looks up the CLSID from the ProgID, then gets the running COM object.
    /// </summary>
    private static object GetActiveObject(string progId)
    {
        int hr = CLSIDFromProgID(progId, out Guid clsid);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
        return obj;
    }

    /// <summary>
    /// Gets the SAP GUI Scripting Engine from the running SAP GUI process.
    /// Equivalent to: winax.GetObject("SAPGUI").GetScriptingEngine
    /// </summary>
    public static dynamic GetScriptingEngine()
    {
        try
        {
            Log.Debug("Acquiring SAP GUI COM object...");
            dynamic sapGui = GetActiveObject("SAPGUI");
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
