namespace SapAdapter.Commands;

/// <summary>
/// Interface for all SAP command handlers.
/// Each handler processes a specific command type and returns a result dictionary.
/// </summary>
public interface ICommandHandler
{
    Task<Dictionary<string, object?>> Execute(dynamic session, Dictionary<string, object?>? payload);
}
