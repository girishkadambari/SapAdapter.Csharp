using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Commands;

/// <summary>
/// Checks for modal windows before command execution.
/// Throws SapException with MODAL_PRESENT if a modal is detected.
/// </summary>
public static class ModalChecker
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ModalChecker));

    public static void Check(dynamic session)
    {
        try
        {
            var children = session.Children;
            int count = children.Count;

            for (int i = 0; i < count; i++)
            {
                var wnd = children.Item(i);
                string type = SafeCom.Execute(() => (string)wnd.Type, "get wnd type");
                string id = SafeCom.Execute(() => (string)wnd.Id, "get wnd id");

                if (type == "GuiModalWindow" || id.Contains("wnd[1]"))
                {
                    string text = SafeCom.Execute(() => (string)wnd.Text, "get modal text");
                    Log.Warning("Modal window detected: {ModalText} (id: {ModalId})", text, id);
                    throw new Models.SapException(
                        Models.SapErrorCodes.ModalPresent,
                        $"Modal window detected: {text}",
                        new { modalId = id }
                    );
                }
            }
        }
        catch (Models.SapException) { throw; }
        catch (Exception ex)
        {
            Log.Debug(ex, "Modal check encountered non-critical error");
        }
    }
}
