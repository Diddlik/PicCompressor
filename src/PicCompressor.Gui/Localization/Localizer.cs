using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace PicCompressor.Gui.Localization;

public enum AppLanguage
{
    /// <summary>Betriebssystemsprache; ohne Übersetzung fällt die Anwendung auf Englisch zurück.</summary>
    System,
    English,
    German
}

/// <summary>
/// Einzige Quelle für Oberflächentexte. Alle sichtbaren Zeichenketten stammen aus
/// <c>Localization/Strings*.resx</c>; ein Sprachwechsel wirkt sofort, ohne Neustart.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new(
        "PicCompressor.Gui.Localization.Strings",
        typeof(Localizer).Assembly);

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    private readonly Dictionary<string, LocalizedString> boundStrings = new(StringComparer.Ordinal);
    private readonly List<WeakReference<INotifyLanguageChanged>> listeners = [];

    private AppLanguage language = AppLanguage.System;
    private CultureInfo culture = ResolveCulture(AppLanguage.System);

    private Localizer() => Apply(culture);

    public static Localizer Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language
    {
        get => language;
        set
        {
            if (language == value)
            {
                return;
            }

            language = value;
            culture = ResolveCulture(value);
            Apply(culture);

            // Leerer Name bedeutet „alle Eigenschaften“ und erneuert damit jede Textbindung.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

            LocalizedString[] bound;
            lock (boundStrings)
            {
                bound = [.. boundStrings.Values];
            }

            foreach (var entry in bound)
            {
                entry.Refresh();
            }

            NotifyListeners();
        }
    }

    public CultureInfo Culture => culture;

    public string this[string key] => Resources.GetString(key, culture) ?? key;

    public string Format(string key, params object?[] arguments) =>
        string.Format(culture, this[key], arguments);

    /// <summary>
    /// Bindbarer Text für einen Ressourcenschlüssel. Je Schlüssel existiert genau eine Instanz,
    /// die nach einem Sprachwechsel ihren Wert neu meldet.
    /// </summary>
    public LocalizedString GetBound(string key)
    {
        lock (boundStrings)
        {
            if (!boundStrings.TryGetValue(key, out var bound))
            {
                bound = new LocalizedString(key);
                boundStrings.Add(key, bound);
            }

            return bound;
        }
    }

    /// <summary>
    /// Registriert ein Objekt, das nach einem Sprachwechsel selbst berechnete Texte neu melden muss.
    /// Die Referenz ist schwach, damit entfernte Warteschlangeneinträge nicht am Leben bleiben.
    /// </summary>
    public void Register(INotifyLanguageChanged listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (listeners)
        {
            listeners.Add(new WeakReference<INotifyLanguageChanged>(listener));
        }
    }

    private void NotifyListeners()
    {
        INotifyLanguageChanged[] alive;
        lock (listeners)
        {
            alive = listeners
                .Select(reference => reference.TryGetTarget(out var target) ? target : null)
                .OfType<INotifyLanguageChanged>()
                .ToArray();
            listeners.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var listener in alive)
        {
            listener.OnLanguageChanged();
        }
    }

    private static CultureInfo ResolveCulture(AppLanguage language) => language switch
    {
        AppLanguage.English => English,
        AppLanguage.German => German,
        // Systemsprache nur übernehmen, wenn dafür Übersetzungen vorliegen.
        _ => IsGerman(CultureInfo.InstalledUICulture) ? German : English
    };

    private static bool IsGerman(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase);

    private static void Apply(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

public interface INotifyLanguageChanged
{
    void OnLanguageChanged();
}

public sealed class LocalizedString(string key) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value => Localizer.Instance[key];

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
