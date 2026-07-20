using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Größenangaben in der aktiven Kultur (Abschnitt 11.2). Die Einheitenkürzel sind
/// international gebräuchlich und werden daher nicht übersetzt.
/// </summary>
internal static class ByteFormat
{
    private static readonly string[] Units = ["B", "kB", "MB", "GB", "TB"];

    public const string NotApplicable = "—";

    public static string Describe(long bytes)
    {
        if (bytes < 0)
        {
            return NotApplicable;
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var culture = Localizer.Instance.Culture;
        return unit == 0
            ? $"{bytes.ToString(culture)} {Units[0]}"
            : $"{value.ToString(value < 10 ? "0.0" : "0", culture)} {Units[unit]}";
    }

    /// <summary>
    /// Einsparung als Anteil der Eingabegröße. Negative Werte bedeuten eine größere Ausgabe und
    /// werden als solche gekennzeichnet, nie beschönigt.
    /// </summary>
    public static string DescribeSavings(long inputBytes, long outputBytes)
    {
        if (inputBytes <= 0)
        {
            return NotApplicable;
        }

        var culture = Localizer.Instance.Culture;
        var reduction = (inputBytes - outputBytes) * 100d / inputBytes;
        return reduction >= 0
            ? $"−{reduction.ToString("0.#", culture)} %"
            : $"+{(-reduction).ToString("0.#", culture)} %";
    }
}
