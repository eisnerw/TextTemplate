// Handwritten equivalents of the legacy Externals.ts examples (Apache-2.0).
// Kept outside Generated/ so regenerating templates cannot overwrite extensions.
namespace TextTemplateExtensions;

public static class Externals
{
    public static TemplateValue WrapUpper(ExtensionCall call)
    {
        string receiver = call.Receiver.TryGetString(out string text)
            ? text : call.Receiver.RenderWithMissingValues();
        // The legacy method evaluates arguments even when their count is invalid.
        var arguments = new string[call.Arguments.Count];
        for (int i = 0; i < arguments.Length; i++)
            arguments[i] = call.Arguments.Evaluate(i).ToJavaScriptString();
        if (arguments.Length != 2)
        {
            call.ReportError("Invalid parameters for test function");
            return TemplateValue.From(receiver);
        }
        return TemplateValue.From(arguments[0] + TemplateValue.Uppercase(receiver) + arguments[1]);
    }

    public static TemplateValue PrefixString(ValueLookup lookup)
    {
        TemplateValue value = lookup.DefaultValue();
        return value.TryGetString(out string text) ? TemplateValue.From(">>" + text) : value;
    }
}
