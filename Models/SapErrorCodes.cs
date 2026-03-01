namespace SapAdapter.Models;

/// <summary>
/// All SAP error codes used across the adapter.
/// Maps 1:1 to the TypeScript SapErrorCodes enum.
/// </summary>
public static class SapErrorCodes
{
    public const string SapNotRunning = "SAP_NOT_RUNNING";
    public const string ScriptingDisabled = "SCRIPTING_DISABLED";
    public const string SessionNotFound = "SESSION_NOT_FOUND";
    public const string ComError = "COM_ERROR";
    public const string Timeout = "TIMEOUT";
    public const string Busy = "BUSY";
    public const string UnsupportedShell = "UNSUPPORTED_SHELL";
    public const string CommandFailed = "COMMAND_FAILED";
    public const string ModalPresent = "MODAL_PRESENT";
    public const string UnhealthySession = "UNHEALTHY_SESSION";
    public const string UnknownRequest = "UNKNOWN_REQUEST";
    public const string ServerError = "SERVER_ERROR";
}

/// <summary>
/// Typed exception for SAP adapter errors with structured error code.
/// </summary>
public class SapException : Exception
{
    public string Code { get; }
    public object? Details { get; }

    public SapException(string code, string message, object? details = null)
        : base($"[{code}] {message}")
    {
        Code = code;
        Details = details;
    }
}
