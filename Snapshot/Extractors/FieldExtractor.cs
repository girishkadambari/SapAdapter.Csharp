using SapAdapter.Com;
using Serilog;

namespace SapAdapter.Snapshot.Extractors;

/// <summary>
/// Recursively extracts field values from the SAP GUI user area.
/// Handles GuiTextField, GuiCTextField, GuiCheckBox, GuiLabel, GuiShell, and containers.
/// </summary>
public static class FieldExtractor
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(FieldExtractor));

    public static Dictionary<string, object> Extract(ExtractorContext ctx, dynamic parent)
    {
        ctx.ExtractorsRun.Add("extractFields");
        var fields = new Dictionary<string, object>();
        ExtractRecursive(ctx, parent, fields);
        Log.Debug("Extracted {Count} fields", fields.Count);
        return fields;
    }

    private static void ExtractRecursive(ExtractorContext ctx, dynamic parent, Dictionary<string, object> fields)
    {
        var children = parent.Children;
        int count = children.Count;

        for (int i = 0; i < count; i++)
        {
            if (!ctx.Budget.IsWithinBudget()) break;

            var child = children.Item(i);
            string type = SafeCom.Execute(() => (string)child.Type, "get child type");
            string id = SafeCom.Execute(() => (string)child.Id, "get id");

            ctx.Budget.NodeCount++;

            try
            {
                switch (type)
                {
                    case "GuiTextField":
                    case "GuiCTextField":
                    case "GuiPasswordField":
                        fields[id] = new Models.SapFieldValue
                        {
                            Value = SafeCom.Execute(() => (string)child.Text, "get value"),
                            Kind = "text",
                            Editable = SafeCom.Execute(() => (bool)child.Changeable, "get changeable"),
                            Label = SafeCom.Execute(() => (string)child.Name, "get label"),
                            Visible = true
                        };
                        break;

                    case "GuiCheckBox":
                        fields[id] = new Models.SapFieldValue
                        {
                            Value = SafeCom.Execute(() => (bool)child.Selected, "get selected"),
                            Kind = "checkbox",
                            Editable = SafeCom.Execute(() => (bool)child.Changeable, "get changeable"),
                            Label = SafeCom.Execute(() => (string)child.Text, "get check label"),
                            Visible = true
                        };
                        break;

                    case "GuiLabel":
                        fields[id] = new Models.SapFieldValue
                        {
                            Value = SafeCom.Execute(() => (string)child.Text, "get label text"),
                            Kind = "label",
                            Visible = true
                        };
                        break;

                    case "GuiShell":
                        var shellVal = ShellExtractor.ExtractShell(ctx, child);
                        if (shellVal != null) fields[id] = shellVal;
                        break;

                    default:
                        // Container — recurse into children
                        try
                        {
                            if (child.Children != null)
                            {
                                ExtractRecursive(ctx, child, fields);
                            }
                        }
                        catch { /* No children */ }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to extract field {Id} (type: {Type}): {Error}", id, type, ex.Message);
                ctx.Warnings.Add($"Field extraction failed for {id}: {ex.Message}");
            }
        }
    }
}
