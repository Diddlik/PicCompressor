using PicCompressor.Domain;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Engine-Bezeichner sind stabile Kennungen und werden nicht übersetzt (Abschnitt 4.3).
/// </summary>
public static class EngineIds
{
    public const string Jpegli = JpegliSettings.JpegliEngineId;

    /// <summary>Alte History-Einträge behalten ihren ursprünglichen Engine-Namen.</summary>
    public static string DisplayName(string engineId) =>
        string.Equals(engineId, "guetzli", StringComparison.OrdinalIgnoreCase)
            ? "Guetzli"
            : "Jpegli";
}
