// Add registrations here; template names and C# method names need not match.
public static partial class TemplateExtensionRegistration
{
    static partial void Configure(TemplateExtensions extensions)
    {
        extensions
            .AddMethod("test", TextTemplateExtensions.Externals.WrapUpper)
            .AddValueFunction("test", TextTemplateExtensions.Externals.PrefixString);
    }
}
