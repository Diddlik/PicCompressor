using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PicCompressor.Gui.Converters;

/// <summary>Konverter für den Redesign-Look (UI-Doc 01/02), die nicht rein per Style gehen.</summary>
public static class NavConverters
{
    private static readonly IBrush ActiveChip = new SolidColorBrush(Color.Parse("#B9EE43"));
    private static readonly IBrush InactiveChip = new SolidColorBrush(Color.Parse("#EDE7DA"));

    /// <summary>Aktiver Navigationseintrag: Lime-Chip, sonst neutral.</summary>
    public static readonly IValueConverter ChipBrush =
        new FuncValueConverter<bool, IBrush>(on => on ? ActiveChip : InactiveChip);
}
