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
    /// Tries multiple COM strategies to handle different SAP GUI versions.
    /// </summary>
    public static dynamic GetScriptingEngine()
    {
        // Strategy 1: Direct SAPGUI ProgID (SAP GUI 7.x and some 8.x)
        var result = TryGetEngine("SAPGUI", sapGui => sapGui.GetScriptingEngine);
        if (result != null) return result;

        // Strategy 2: SapROTWr.SapROTWrapper (SAP GUI 8.x+ / newer installations)
        result = TryGetEngine("SapROTWr.SapROTWrapper", wrapper =>
        {
            dynamic rot = wrapper;
            dynamic sapGui = rot.GetROTEntry("SAPGUI");
            if (sapGui == null)
                throw new COMException("SAPGUI not found in ROT");
            return sapGui.GetScriptingEngine();
        });
        if (result != null) return result;

        // Strategy 3: SAPGUISERVER ProgID (rare, server-side installations)
        result = TryGetEngine("SAPGUISERVER", sapGui => sapGui.GetScriptingEngine);
        if (result != null) return result;

        Log.Error("All COM strategies failed — SAP GUI may not be running or scripting is disabled");
        throw new Models.SapException(
            Models.SapErrorCodes.SapNotRunning,
            "Cannot connect to SAP GUI. Ensure SAP GUI is running and scripting is enabled " +
            "(Options → Accessibility & Scripting → Scripting → Enable Scripting)."
        );
    }

    /// <summary>
    /// Attempts to get the scripting engine using a specific ProgID.
    /// Returns null if the ProgID is not found or the object can't be retrieved.
    /// </summary>
    private static dynamic? TryGetEngine(string progId, Func<dynamic, dynamic> extractor)
    {
        try
        {
            Log.Debug("Trying COM ProgID: {ProgId}", progId);
            dynamic comObj = GetActiveObject(progId);
            dynamic engine = extractor(comObj);
            Log.Information("SAP GUI Scripting Engine acquired via {ProgId}", progId);
            return engine;
        }
        catch (COMException ex)
        {
            Log.Debug("ProgID {ProgId} failed: {Error}", progId, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("ProgID {ProgId} failed (non-COM): {Error}", progId, ex.Message);
            return null;
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
