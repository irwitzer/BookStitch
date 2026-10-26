using System.Windows.Markup;

namespace BookStitch.Services;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizedToolTipExtension : MarkupExtension
{
    public LocalizedToolTipExtension()
    {
    }

    public LocalizedToolTipExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        ToolTipTextService.Get(Key);
}
