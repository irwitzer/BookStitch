using System.Windows.Markup;

namespace BookStitch.Services;

/// <summary>
/// Gives XAML access to <see cref="LocalizationText"/>, for example
/// <c>Text="{services:LocalizedText Key=LanguageHeading}"</c>.
/// The UI culture is fixed once during startup, so resolving the value at load time is sufficient
/// and mirrors how <see cref="LocalizedToolTipExtension"/> already works.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizedTextExtension : MarkupExtension
{
    public LocalizedTextExtension()
    {
    }

    public LocalizedTextExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationText.Get(Key);
}
