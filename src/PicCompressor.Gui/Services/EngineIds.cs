using PicCompressor.Domain;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Engine-Bezeichner sind stabile Kennungen und werden nicht übersetzt (Abschnitt 4.3).
/// </summary>
public static class EngineIds
{
    public const string Jpegli = JpegliSettings.JpegliEngineId;
    public const string Guetzli = GuetzliSettings.GuetzliEngineId;

    /// <summary>
    /// Untergrenze der offiziellen Guetzli-Revision (Abschnitt 5.2). Sobald der Engine-Katalog
    /// eine reale Capability liefert, ersetzt deren Wert diese Annahme.
    /// </summary>
    public const int GuetzliMinimumQuality = GuetzliSettings.MinimumQuality;

    /// <summary>Anzeigename; Eigenname und daher unübersetzt.</summary>
    public static string DisplayName(string engineId) =>
        engineId == Guetzli ? "Guetzli" : "Jpegli";
}
