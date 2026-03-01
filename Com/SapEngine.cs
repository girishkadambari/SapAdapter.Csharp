using System.Runtime.InteropServices;
using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Provides access to the SAP GUI Scripting Engine via COM interop.
/// Uses P/Invoke to oleaut32.dll since Marshal.GetActiveObject was removed in .NET 5+.
/// Supports multiple SAP GUI versions (7.x, 8.x+) via different COM strategies.
/// </summary>
public static class SapEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SapEngine));

    // P/Invoke declarations for COM activation (replaces Marshal.GetActiveObject in .NET 8)
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid lpclsid);

    /// <summary>
    /// Equivalent to Marshal.GetActiveObject(progId) — works on .NET 8.
    /// Looks up the CLSID from the ProgID, then gets the running COM object.
    /// </summary>
    private static object MarshalGetActiveObject(string progId)
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
        try
        {
            Log.Debug("Trying direct ProgID: SAPGUI...");
            dynamic sapGui = MarshalGetActiveObject("SAPGUI");
            dynamic engine = sapGui.GetScriptingEngine;
            Log.Information("SAP GUI Scripting Engine acquired via SAPGUI ProgID");
            return engine;
        }
        catch (Exception ex)
        {
            Log.Debug("SAPGUI ProgID failed: {Error}", ex.Message);
        }

        // Strategy 2: SapROTWr.SapROTWrapper — the official SAP approach for newer versions.
        // IMPORTANT: This is a COM class you CREATE (Activator.CreateInstance),
        // NOT a running object you look up with GetActiveObject.
        // Then call GetROTEntry("SAPGUI") to find the SAP GUI in the Running Object Table.
        try
        {
            Log.Debug("Trying SapROTWr.SapROTWrapper via Activator.CreateInstance...");

            Type? rotType = Type.GetTypeFromProgID("SapROTWr.SapROTWrapper");
            if (rotType != null)
            {
                dynamic rotWrapper = Activator.CreateInstance(rotType)!;
                dynamic sapGui = rotWrapper.GetROTEntry("SAPGUI");
                if (sapGui != null)
                {
                    dynamic engine = sapGui.GetScriptingEngine();
                    Log.Information("SAP GUI Scripting Engine acquired via SapROTWr.SapROTWrapper");
                    return engine;
                }
                else
                {
                    Log.Debug("SAPGUI entry not found in ROT (SAP GUI may not be running)");
                }
            }
            else
            {
                Log.Debug("SapROTWr.SapROTWrapper ProgID not registered, trying DLL from disk...");

                // Fallback: load SapROTWr.dll directly from SAP GUI installation folder
                var rotTypeFromDisk = TryLoadROTWrapperFromDisk();
                if (rotTypeFromDisk != null)
                {
                    dynamic rotWrapper = Activator.CreateInstance(rotTypeFromDisk)!;
                    dynamic sapGui = rotWrapper.GetROTEntry("SAPGUI");
                    if (sapGui != null)
                    {
                        dynamic engine = sapGui.GetScriptingEngine();
                        Log.Information("SAP GUI Scripting Engine acquired via SapROTWr.dll (loaded from disk)");
                        return engine;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SapROTWr strategy failed: {Error}", ex.Message);
        }

        // Strategy 3: SAPGUISERVER ProgID (rare, server-side installations)
        try
        {
            Log.Debug("Trying direct ProgID: SAPGUISERVER...");
            dynamic sapGui = MarshalGetActiveObject("SAPGUISERVER");
            dynamic engine = sapGui.GetScriptingEngine;
            Log.Information("SAP GUI Scripting Engine acquired via SAPGUISERVER ProgID");
            return engine;
        }
        catch (Exception ex)
        {
            Log.Debug("SAPGUISERVER ProgID failed: {Error}", ex.Message);
        }

        Log.Error("All COM strategies failed — SAP GUI may not be running or scripting is disabled");
        throw new Models.SapException(
            Models.SapErrorCodes.SapNotRunning,
            "Cannot connect to SAP GUI. Ensure SAP GUI is running and scripting is enabled. " +
            "If needed, register the ROT wrapper as Admin: regsvr32 \"C:\\Program Files (x86)\\SAP\\FrontEnd\\SAPgui\\SapROTWr.dll\""
        );
    }

    /// <summary>
    /// Searches for SapROTWr.dll in common SAP GUI installation directories.
    /// </summary>
    private static Type? TryLoadROTWrapperFromDisk()
    {
        var searchPaths = new[]
        {
            @"C:\Program Files (x86)\SAP\FrontEnd\SAPgui\SapROTWr.dll",
            @"C:\Program Files\SAP\FrontEnd\SAPgui\SapROTWr.dll",
        };

        foreach (var dllPath in searchPaths)
        {
            if (File.Exists(dllPath))
            {
                Log.Information("Found SapROTWr.dll at {Path}", dllPath);
                try
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                    var type = assembly.GetType("SapROTWr.SapROTWrapper")
                            ?? assembly.GetTypes().FirstOrDefault(t => t.Name.Contains("ROTWrapper"));
                    if (type != null) return type;
                }
                catch (Exception ex)
                {
                    Log.Debug("Failed to load DLL: {Error}", ex.Message);
                }
            }
        }
        return null;
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
