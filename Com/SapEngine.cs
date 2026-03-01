using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Serilog;

namespace SapAdapter.Com;

/// <summary>
/// Provides access to the SAP GUI Scripting Engine via COM interop.
/// Uses Marshal.GetActiveObject (available on .NET Framework 4.8).
/// </summary>
public static class SapEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SapEngine));

    /// <summary>
    /// Gets the SAP GUI Scripting Engine from the running SAP GUI process.
    /// Tries multiple COM strategies to handle different SAP GUI versions.
    /// </summary>
    public static dynamic GetScriptingEngine()
    {
        // Strategy 1: Direct SAPGUI ProgID via Marshal.GetActiveObject
        try
        {
            Log.Debug("Trying Marshal.GetActiveObject(\"SAPGUI\")...");
            dynamic sapGui = Marshal.GetActiveObject("SAPGUI");
            dynamic engine = sapGui.GetScriptingEngine;
            Log.Information("SAP GUI Scripting Engine acquired via Marshal.GetActiveObject");
            return engine;
        }
        catch (COMException ex)
        {
            Log.Debug("Direct SAPGUI ProgID failed: {Error}", ex.Message);
        }

        // Strategy 2: SapROTWr.SapROTWrapper — create instance, then look up in ROT
        try
        {
            Log.Debug("Trying SapROTWr.SapROTWrapper...");

            Type? rotType = Type.GetTypeFromProgID("SapROTWr.SapROTWrapper");
            if (rotType == null)
            {
                Log.Debug("SapROTWr ProgID not registered, searching SAP GUI folder...");
                rotType = TryLoadROTWrapperFromDisk();
            }

            if (rotType != null)
            {
                dynamic rotWrapper = Activator.CreateInstance(rotType)!;
                dynamic sapGui = rotWrapper.GetROTEntry("SAPGUI");
                if (sapGui != null)
                {
                    dynamic engine = sapGui.GetScriptingEngine();
                    Log.Information("SAP GUI Scripting Engine acquired via SapROTWr");
                    return engine;
                }
                Log.Debug("SAPGUI entry not found in ROT");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SapROTWr strategy failed: {Error}", ex.Message);
        }

        // Strategy 3: SAPGUISERVER ProgID
        try
        {
            Log.Debug("Trying Marshal.GetActiveObject(\"SAPGUISERVER\")...");
            dynamic sapGui = Marshal.GetActiveObject("SAPGUISERVER");
            dynamic engine = sapGui.GetScriptingEngine;
            Log.Information("SAP GUI Scripting Engine acquired via SAPGUISERVER");
            return engine;
        }
        catch (COMException ex)
        {
            Log.Debug("SAPGUISERVER ProgID failed: {Error}", ex.Message);
        }

        Log.Error("All COM strategies failed — SAP GUI may not be running or scripting is disabled");
        throw new Models.SapException(
            Models.SapErrorCodes.SapNotRunning,
            "Cannot connect to SAP GUI. Ensure SAP GUI is running and scripting is enabled. " +
            "If needed, register SapROTWr.dll: regsvr32 \"C:\\Program Files (x86)\\SAP\\FrontEnd\\SAPgui\\SapROTWr.dll\""
        );
    }

    /// <summary>
    /// Fallback: Search for SapROTWr.dll in common SAP GUI installation directories.
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
