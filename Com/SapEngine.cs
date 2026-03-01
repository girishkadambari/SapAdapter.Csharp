using Serilog;
using System.Runtime.InteropServices.ComTypes;

namespace SapAdapter.Com;

/// <summary>
/// Provides access to the SAP GUI Scripting Engine via COM interop.
/// Uses native Marshal.GetActiveObject available on .NET Framework 4.8.
/// </summary>
public static class SapEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(SapEngine));

    // P/Invoke for manual ROT access in case GetActiveObject fails
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    /// <summary>
    /// Gets the SAP GUI Scripting Engine from the running SAP GUI process.
    /// Tries multiple COM strategies to handle different SAP GUI versions.
    /// </summary>
    public static dynamic GetScriptingEngine()
    {
        return SafeCom.Execute(DiscoverScriptingEngine, "Discover SAP Scripting Engine");
    }

    private static dynamic DiscoverScriptingEngine()
    {
        // Check if SAP is running at all
        var sapProcs = System.Diagnostics.Process.GetProcessesByName("saplogon")
            .Concat(System.Diagnostics.Process.GetProcessesByName("sapgui")).ToList();
        
        Log.Information("Process Check: Found {Count} SAP processes ({Names})", 
            sapProcs.Count, 
            string.Join(", ", sapProcs.Select(p => p.ProcessName).Distinct()));

        if (!sapProcs.Any())
        {
            Log.Warning("No 'saplogon' or 'sapgui' processes found. Is SAP GUI open?");
        }

        // We try these names in order
        var names = new[] { "SapGui.Application", "SAPGUI", "SAPGUISERVER" };

        foreach (var name in names)
        {
            // Strategy 1: Direct Marshal.GetActiveObject
            try
            {
                Log.Debug("Trying direct Marshal.GetActiveObject: {Name}...", name);
                dynamic sapApp = Marshal.GetActiveObject(name);
                var engine = ExtractEngine(sapApp);
                if (engine != null)
                {
                    Log.Information("SAP GUI Scripting Engine acquired via native Marshal: {Name}", name);
                    return engine;
                }
            }
            catch (COMException ex)
            {
                Log.Information("Native Marshal {Name} failed. HRESULT: 0x{HR:X8} ({Msg})", name, ex.HResult, ex.Message);
            }
            catch (Exception ex)
            {
                Log.Debug("Native Marshal {Name} failed: {Error}", name, ex.Message);
            }

            // Strategy 2: ROT Wrapper lookup
            try
            {
                Log.Debug("Trying ROT Wrapper lookup for: {Name}...", name);
                var rotType = GetROTWrapperType();
                if (rotType != null)
                {
                    dynamic rotWrapper = Activator.CreateInstance(rotType)!;
                    dynamic sapApp = rotWrapper.GetROTEntry(name);
                    if (sapApp != null)
                    {
                        var engine = ExtractEngine(sapApp);
                        if (engine != null)
                        {
                            Log.Information("SAP GUI Scripting Engine acquired via ROT Wrapper: {Name}", name);
                            return engine;
                        }
                    }
                    else
                    {
                        Log.Information("ROT Wrapper: GetROTEntry('{Name}') returned null", name);
                    }
                }
            }
            catch (COMException ex)
            {
                Log.Information("ROT Wrapper lookup for {Name} failed. HRESULT: 0x{HR:X8}", name, ex.HResult);
            }
            catch (Exception ex)
            {
                Log.Debug("ROT Wrapper lookup for {Name} failed: {Error}", name, ex.Message);
            }
        }

        // Strategy 3: Manual ROT Enumeration (The "Deep Search")
        Log.Information("Searching Running Object Table (ROT) manually...");
        try 
        {
            var manuallyFound = FindInROT();
            if (manuallyFound != null)
            {
                var engine = ExtractEngine(manuallyFound);
                if (engine != null) return engine;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Manual ROT search failed: {Error}", ex.Message);
        }

        // Strategy 4: Last Resort - Create Instance
        // Sometimes we need to create the instance to "kickstart" the COM server
        try
        {
            Log.Information("Attempting to CreateInstance of SapGui.Application as last resort...");
            Type? sapType = Type.GetTypeFromProgID("SapGui.Application");
            if (sapType != null)
            {
                dynamic sapApp = Activator.CreateInstance(sapType)!;
                var engine = ExtractEngine(sapApp);
                if (engine != null)
                {
                    Log.Information("SAP GUI Scripting Engine acquired via CreateInstance");
                    return engine;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("CreateInstance fallback failed: {Error}", ex.Message);
        }

        Log.Error("All COM detection strategies failed. SAP GUI may not be running or scripting is disabled.");
        throw new Models.SapException(
            Models.SapErrorCodes.SapNotRunning,
            "Cannot connect to SAP GUI. Ensure:\n" +
            "1. SAP GUI is running and logged in.\n" +
            "2. Scripting is enabled in SAP GUI (Options > Accessibility & Scripting > Scripting).\n" +
            "3. Adapter and SAP GUI are running as the same user."
        );
    }

    private static dynamic? FindInROT()
    {
        GetRunningObjectTable(0, out var rot);
        rot.EnumRunning(out var enumMoniker);
        IMoniker[] monikers = new IMoniker[1];
        IntPtr fetched = IntPtr.Zero;

        while (enumMoniker.Next(1, monikers, fetched) == 0)
        {
            CreateBindCtx(0, out var bindCtx);
            monikers[0].GetDisplayName(bindCtx, null, out var displayName);

            if (displayName.Contains("SAPGUI") || displayName.Contains("SapGui") || displayName.Contains("sapgui"))
            {
                Log.Information("Found candidate in ROT: {Name}", displayName);
                rot.GetObject(monikers[0], out var obj);
                if (obj != null) return obj;
            }
        }
        return null;
    }

    /// <summary>
    /// Handles both property and method access for GetScriptingEngine.
    /// </summary>
    private static dynamic? ExtractEngine(dynamic sapApp)
    {
        if (sapApp == null) return null;
        try { return sapApp.GetScriptingEngine(); }
        catch 
        {
            try { return sapApp.GetScriptingEngine; }
            catch { return null; }
        }
    }

    private static Type? GetROTWrapperType()
    {
        Type? type = Type.GetTypeFromProgID("SapROTWr.SapROTWrapper");
        if (type != null) return type;
        return TryLoadROTWrapperFromDisk();
    }

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
                try
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                    var type = assembly.GetType("SapROTWr.SapROTWrapper")
                            ?? assembly.GetTypes().FirstOrDefault(t => t.Name.Contains("ROTWrapper"));
                    if (type != null) return type;
                }
                catch { }
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
            var conn = SafeCom.Execute(() => engine.Children(connectionIndex), "get connection " + connectionIndex);
            var session = SafeCom.Execute(() => conn.Children(sessionIndex), "get session " + sessionIndex);
            return session;
        }
        catch (Models.SapException) { throw; }
        catch (Exception ex)
        {
            throw new Models.SapException(
                Models.SapErrorCodes.SessionNotFound,
                $"Session {connectionIndex}/{sessionIndex} not found: {ex.Message}"
            );
        }
    }
}
