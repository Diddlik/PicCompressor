using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Effektive Kompressionseinstellungen der GUI. Bildet die normativen Produktparameter
/// aus docs/requirements.md Abschnitt 5.1, 7.2, 8.1 und 8.3 ab; die UI-Vorlage liefert
/// nur das visuelle Zielbild.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly HashSet<string> availableEngines = new(StringComparer.Ordinal);

    private string engineId = EngineIds.Jpegli;
    private int quality = 90;
    private JpegliChromaSubsampling chromaSubsampling = JpegliChromaSubsampling.Subsampling420;
    private int progressiveLevel = 2;
    private OutputTarget outputTarget = OutputTarget.SuffixNextToInput;
    private string suffix = "_compressed";
    private string? outputDirectory;
    private CollisionPolicy collisionPolicy = CollisionPolicy.Skip;
    private LargerOutputPolicy largerOutputPolicy = LargerOutputPolicy.Discard;
    private ExifPolicy exifPolicy = ExifPolicy.Remove;
    private ColorProfilePolicy colorProfilePolicy = ColorProfilePolicy.Preserve;
    private RgbColor alphaBackground = RgbColor.White;
    private int parallelJobs = Math.Max(1, Environment.ProcessorCount / 2);
    private int historyRetentionDays = 90;

    private readonly IApplicationSettingsStore settingsStore;
    private ApplicationSettings stored;
    private bool applyingStoredSettings;

    /// <summary>
    /// Ohne übergebenen Speicher bleiben die Einstellungen auf die Sitzung beschränkt.
    /// Der persistente Speicher wird vom Desktop Host verdrahtet; die GUI kennt nur den
    /// Application-Port, nicht die Infrastruktur (Abschnitt 14.1).
    /// </summary>
    public SettingsViewModel(IApplicationSettingsStore? settingsStore = null)
    {
        this.settingsStore = settingsStore ?? new InMemoryApplicationSettingsStore();
        stored = this.settingsStore.Load();
        ApplyStoredSettings(stored);

        PropertyChanged += (_, _) => PersistSettings();
        Appearance.PropertyChanged += (_, _) => PersistSettings();
    }

    public string EngineId
    {
        get => engineId;
        set
        {
            if (SetProperty(ref engineId, value))
            {
                Raise(nameof(IsJpegli));
                Raise(nameof(IsGuetzli));
                Raise(nameof(MinQuality));
                Raise(nameof(EngineDescription));
                Raise(nameof(IsSelectedEngineAvailable));
                Raise(nameof(EngineAvailabilityText));
                Quality = Math.Max(MinQuality, Quality);
            }
        }
    }

    public bool IsJpegli
    {
        get => EngineId == EngineIds.Jpegli;
        set { if (value) { EngineId = EngineIds.Jpegli; } }
    }

    public bool IsGuetzli
    {
        get => EngineId == EngineIds.Guetzli;
        set { if (value) { EngineId = EngineIds.Guetzli; } }
    }

    /// <summary>Guetzli-Untergrenze nach Abschnitt 5.2; der reale Wert kommt aus der Engine-Capability.</summary>
    public int MinQuality => IsGuetzli ? EngineIds.GuetzliMinimumQuality : 1;

    public string EngineDescription => Localizer.Instance[
        IsGuetzli ? "Engine_GuetzliDescription" : "Engine_JpegliDescription"];

    public int Quality
    {
        get => quality;
        set => SetProperty(ref quality, Math.Clamp(value, MinQuality, 100));
    }

    public JpegliChromaSubsampling ChromaSubsampling
    {
        get => chromaSubsampling;
        set
        {
            if (SetProperty(ref chromaSubsampling, value))
            {
                Raise(nameof(Is444));
                Raise(nameof(Is440));
                Raise(nameof(Is422));
                Raise(nameof(Is420));
            }
        }
    }

    public bool Is444
    {
        get => ChromaSubsampling is JpegliChromaSubsampling.Subsampling444;
        set { if (value) { ChromaSubsampling = JpegliChromaSubsampling.Subsampling444; } }
    }

    public bool Is440
    {
        get => ChromaSubsampling is JpegliChromaSubsampling.Subsampling440;
        set { if (value) { ChromaSubsampling = JpegliChromaSubsampling.Subsampling440; } }
    }

    public bool Is422
    {
        get => ChromaSubsampling is JpegliChromaSubsampling.Subsampling422;
        set { if (value) { ChromaSubsampling = JpegliChromaSubsampling.Subsampling422; } }
    }

    public bool Is420
    {
        get => ChromaSubsampling is JpegliChromaSubsampling.Subsampling420;
        set { if (value) { ChromaSubsampling = JpegliChromaSubsampling.Subsampling420; } }
    }

    /// <summary>0 = sequenziell, 1..2 = progressiv (Abschnitt 5.1).</summary>
    public int ProgressiveLevel
    {
        get => progressiveLevel;
        set => SetProperty(ref progressiveLevel, Math.Clamp(value, 0, 2));
    }

    public OutputTarget OutputTarget
    {
        get => outputTarget;
        set
        {
            if (SetProperty(ref outputTarget, value))
            {
                Raise(nameof(UsesSuffix));
                Raise(nameof(UsesCustomDirectory));
            }
        }
    }

    public bool UsesSuffix
    {
        get => OutputTarget is OutputTarget.SuffixNextToInput;
        set { if (value) { OutputTarget = OutputTarget.SuffixNextToInput; } }
    }

    public bool UsesCustomDirectory
    {
        get => OutputTarget is OutputTarget.CustomDirectory;
        set { if (value) { OutputTarget = OutputTarget.CustomDirectory; } }
    }

    public string Suffix
    {
        get => suffix;
        set => SetProperty(ref suffix, value);
    }

    public string? OutputDirectory
    {
        get => outputDirectory;
        set => SetProperty(ref outputDirectory, value);
    }

    public CollisionPolicy CollisionPolicy
    {
        get => collisionPolicy;
        set
        {
            if (SetProperty(ref collisionPolicy, value))
            {
                Raise(nameof(CollisionSkip));
                Raise(nameof(CollisionRename));
                Raise(nameof(CollisionOverwrite));
            }
        }
    }

    public bool CollisionSkip
    {
        get => CollisionPolicy is CollisionPolicy.Skip;
        set { if (value) { CollisionPolicy = CollisionPolicy.Skip; } }
    }

    public bool CollisionRename
    {
        get => CollisionPolicy is CollisionPolicy.Rename;
        set { if (value) { CollisionPolicy = CollisionPolicy.Rename; } }
    }

    public bool CollisionOverwrite
    {
        get => CollisionPolicy is CollisionPolicy.Overwrite;
        set { if (value) { CollisionPolicy = CollisionPolicy.Overwrite; } }
    }

    public LargerOutputPolicy LargerOutputPolicy
    {
        get => largerOutputPolicy;
        set
        {
            if (SetProperty(ref largerOutputPolicy, value))
            {
                Raise(nameof(DiscardLargerOutput));
            }
        }
    }

    public bool DiscardLargerOutput
    {
        get => LargerOutputPolicy is LargerOutputPolicy.Discard;
        set => LargerOutputPolicy = value ? LargerOutputPolicy.Discard : LargerOutputPolicy.Keep;
    }

    public ExifPolicy ExifPolicy
    {
        get => exifPolicy;
        set
        {
            if (SetProperty(ref exifPolicy, value))
            {
                Raise(nameof(ExifKeep));
                Raise(nameof(ExifPrivate));
                Raise(nameof(ExifRemove));
            }
        }
    }

    public bool ExifKeep
    {
        get => ExifPolicy is ExifPolicy.Keep;
        set { if (value) { ExifPolicy = ExifPolicy.Keep; } }
    }

    public bool ExifPrivate
    {
        get => ExifPolicy is ExifPolicy.Private;
        set { if (value) { ExifPolicy = ExifPolicy.Private; } }
    }

    public bool ExifRemove
    {
        get => ExifPolicy is ExifPolicy.Remove;
        set { if (value) { ExifPolicy = ExifPolicy.Remove; } }
    }

    public ColorProfilePolicy ColorProfilePolicy
    {
        get => colorProfilePolicy;
        set
        {
            if (SetProperty(ref colorProfilePolicy, value))
            {
                Raise(nameof(ProfilePreserve));
                Raise(nameof(ProfileSrgb));
                Raise(nameof(ProfileRemove));
            }
        }
    }

    public bool ProfilePreserve
    {
        get => ColorProfilePolicy is ColorProfilePolicy.Preserve;
        set { if (value) { ColorProfilePolicy = ColorProfilePolicy.Preserve; } }
    }

    public bool ProfileSrgb
    {
        get => ColorProfilePolicy is ColorProfilePolicy.Srgb;
        set { if (value) { ColorProfilePolicy = ColorProfilePolicy.Srgb; } }
    }

    public bool ProfileRemove
    {
        get => ColorProfilePolicy is ColorProfilePolicy.Remove;
        set { if (value) { ColorProfilePolicy = ColorProfilePolicy.Remove; } }
    }

    /// <summary>Hintergrund für transparente PNG-Eingaben; Standard Weiß (Abschnitt 8.1).</summary>
    public RgbColor AlphaBackground
    {
        get => alphaBackground;
        set
        {
            if (SetProperty(ref alphaBackground, value))
            {
                Raise(nameof(AlphaBackgroundHex));
            }
        }
    }

    public string AlphaBackgroundHex =>
        $"#{AlphaBackground.Red:X2}{AlphaBackground.Green:X2}{AlphaBackground.Blue:X2}";

    public int ParallelJobs
    {
        get => parallelJobs;
        set => SetProperty(ref parallelJobs, Math.Clamp(value, 1, Environment.ProcessorCount));
    }

    public int MaxParallelJobs => Environment.ProcessorCount;

    /// <summary>
    /// Aufbewahrungsdauer des Verlaufs in Tagen (Abschnitt 13.1). Die Änderung wird
    /// gespeichert und beim nächsten Start des Desktop Hosts angewendet. Untergrenze 1,
    /// damit ein versehentlicher Nullwert nicht den gesamten Verlauf verwirft.
    /// </summary>
    public int HistoryRetentionDays
    {
        get => historyRetentionDays;
        set => SetProperty(ref historyRetentionDays, Math.Clamp(value, 1, 3650));
    }

    public AppearanceViewModel Appearance { get; } = new();

    /// <summary>
    /// Übernimmt die gespeicherten Werte. Während der Übernahme wird nicht
    /// zurückgeschrieben, damit das Laden die Datei nicht sofort neu schreibt.
    /// </summary>
    private void ApplyStoredSettings(ApplicationSettings settings)
    {
        applyingStoredSettings = true;
        try
        {
            // Die Engine zuerst: ihr Setter zieht die Qualität auf die Engine-Untergrenze.
            EngineId = settings.EngineId;
            Quality = settings.Quality;
            ChromaSubsampling = settings.ChromaSubsampling;
            ProgressiveLevel = settings.ProgressiveLevel;
            ExifPolicy = settings.ExifPolicy;
            ColorProfilePolicy = settings.ColorProfilePolicy;
            CollisionPolicy = settings.CollisionPolicy;
            LargerOutputPolicy = settings.LargerOutputPolicy;
            Suffix = settings.Suffix;
            OutputDirectory = settings.OutputDirectory;
            OutputTarget = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? OutputTarget.SuffixNextToInput
                : OutputTarget.CustomDirectory;
            ParallelJobs = settings.ParallelJobs;
            HistoryRetentionDays = settings.HistoryRetentionDays;
            Appearance.Language = Parse(settings.Language, AppLanguage.System);
            Appearance.Theme = Parse(settings.Theme, AppTheme.System);
        }
        finally
        {
            applyingStoredSettings = false;
        }
    }

    private void PersistSettings()
    {
        if (applyingStoredSettings)
        {
            return;
        }

        // Aus dem geladenen Datensatz fortschreiben statt neu aufzubauen: Felder ohne
        // Entsprechung in der Oberfläche bleiben so erhalten.
        stored = stored with
        {
            Language = Appearance.Language.ToString(),
            Theme = Appearance.Theme.ToString(),
            EngineId = EngineId,
            Quality = Quality,
            ChromaSubsampling = ChromaSubsampling,
            ProgressiveLevel = ProgressiveLevel,
            ExifPolicy = ExifPolicy,
            ColorProfilePolicy = ColorProfilePolicy,
            CollisionPolicy = CollisionPolicy,
            LargerOutputPolicy = LargerOutputPolicy,
            Suffix = Suffix,
            OutputDirectory = OutputTarget is OutputTarget.CustomDirectory
                ? OutputDirectory
                : null,
            ParallelJobs = ParallelJobs,
            HistoryRetentionDays = HistoryRetentionDays
        };
        settingsStore.Save(stored);
    }

    private static TEnum Parse<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    /// <summary>
    /// Übernimmt das Ergebnis der Engine-Erkennung. Eine nicht verfügbare Engine wird nicht
    /// stillschweigend gewechselt (Abschnitt 4.2); sie bleibt gewählt und wird als nicht
    /// ausführbar ausgewiesen.
    /// </summary>
    public void ApplyEngineAvailability(IEnumerable<EngineAvailability> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);

        availableEngines.Clear();
        foreach (var engine in engines.Where(engine => engine.IsAvailable))
        {
            availableEngines.Add(engine.EngineId);
        }

        Raise(nameof(IsSelectedEngineAvailable));
        Raise(nameof(EngineAvailabilityText));
    }

    public bool IsSelectedEngineAvailable => availableEngines.Contains(EngineId);

    /// <summary>Konkrete Ursache, wenn die gewählte Engine nicht ausführbar ist.</summary>
    public string? EngineAvailabilityText => IsSelectedEngineAvailable
        ? null
        : Localizer.Instance.Format("Engine_Unavailable", EngineIds.DisplayName(EngineId));

    /// <summary>
    /// Baut die enginespezifischen Einstellungen. Für Engines, die das Domain-Modell noch nicht
    /// abbildet, liefert die Methode <c>null</c>; der Aufrufer meldet das als
    /// <see cref="CompressionErrorCategory.EngineUnavailable"/> und behauptet keinen Erfolg.
    /// Die Verfügbarkeit der Engine prüft nicht diese Methode, sondern die Engine-Capability
    /// (Abschnitt 4.2): auch für eine nicht eingebundene Engine entstehen gültige Einstellungen.
    /// </summary>
    public CompressionEngineSettings? TryBuildEngineSettings()
    {
        if (IsJpegli)
        {
            return new JpegliSettings(Quality, ChromaSubsampling, ProgressiveLevel);
        }

        return IsGuetzli ? new GuetzliSettings(Quality) : null;
    }
}

public enum OutputTarget
{
    SuffixNextToInput,
    CustomDirectory
}
