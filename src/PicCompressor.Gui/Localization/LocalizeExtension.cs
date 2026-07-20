using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace PicCompressor.Gui.Localization;

/// <summary>
/// XAML-Kurzform für einen Ressourcentext: <c>{l:Localize Nav_Workspace}</c>.
/// Liefert eine Bindung auf den Localizer, damit ein Sprachwechsel sofort durchschlägt.
/// </summary>
public sealed class LocalizeExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocalizedString.Value))
        {
            Source = Localizer.Instance.GetBound(Key),
            Mode = BindingMode.OneWay
        };
}
