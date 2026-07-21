using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.Tests;

/// <summary>
/// Hält die Lokalisierungsregeln aus agent_instructions.md maschinell nach: gleiche Schlüssel in
/// allen Sprachen, keine leeren Werte, gleiche Platzhalter.
/// </summary>
public sealed class LocalizationConsistencyTests
{
    private static readonly IReadOnlyDictionary<string, string> English = Load("Strings.resx");
    private static readonly IReadOnlyDictionary<string, string> German = Load("Strings.de.resx");

    [Fact]
    public void Both_languages_define_the_same_keys()
    {
        var onlyEnglish = English.Keys.Except(German.Keys).Order().ToList();
        var onlyGerman = German.Keys.Except(English.Keys).Order().ToList();

        Assert.True(
            onlyEnglish.Count == 0 && onlyGerman.Count == 0,
            $"Only in Strings.resx: [{string.Join(", ", onlyEnglish)}]. "
            + $"Only in Strings.de.resx: [{string.Join(", ", onlyGerman)}].");
    }

    [Fact]
    public void No_translation_is_empty()
    {
        var empty = English.Concat(German)
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .Order()
            .ToList();

        Assert.True(empty.Count == 0, $"Empty values: [{string.Join(", ", empty)}]");
    }

    [Fact]
    public void Placeholders_match_across_languages()
    {
        var mismatched = new List<string>();
        foreach (var (key, english) in English)
        {
            if (!German.TryGetValue(key, out var german))
            {
                continue;
            }

            if (!Placeholders(english).SetEquals(Placeholders(german)))
            {
                mismatched.Add(key);
            }
        }

        Assert.True(mismatched.Count == 0, $"Placeholder mismatch: [{string.Join(", ", mismatched)}]");
    }

    [Fact]
    public void Every_job_status_has_a_label()
    {
        foreach (var status in Enum.GetValues<PicCompressor.Domain.JobStatus>())
        {
            Assert.True(
                English.ContainsKey($"Job_{status}"),
                $"Missing resource key Job_{status}.");
        }
    }

    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.German)]
    public void Resource_manager_resolves_every_key(AppLanguage language)
    {
        var previous = Localizer.Instance.Language;
        try
        {
            Localizer.Instance.Language = language;
            foreach (var key in English.Keys)
            {
                // Der Localizer gibt den Schlüssel zurück, wenn keine Ressource gefunden wurde.
                Assert.NotEqual(key, Localizer.Instance[key]);
            }
        }
        finally
        {
            Localizer.Instance.Language = previous;
        }
    }

    [Fact]
    public void German_resources_differ_from_english_where_a_translation_is_expected()
    {
        // Eigennamen und Bezeichner sind absichtlich identisch; alles andere muss übersetzt sein.
        string[] intentionallyIdentical =
        [
            "App_Title", "Engine_Jpegli", "Engine_Guetzli", "Common_Engine",
            "Set_Exif", "Set_LanguageSystem", "Set_ThemeSystem",
            "Hist_ColEngine", "Hist_ColStatus", "Cmp_Original",
            "Status_EnginesAvailable", "Set_EngineHeading",
            // "Zoom" ist im Deutschen dasselbe Wort; der Zoomwert ist ein reines Zahlenformat.
            "Cmp_Zoom", "Cmp_ZoomValue"
        ];

        var identical = English
            .Where(entry => German.TryGetValue(entry.Key, out var german) && german == entry.Value)
            .Select(entry => entry.Key)
            .Except(intentionallyIdentical)
            .Order()
            .ToList();

        Assert.True(
            identical.Count == 0,
            $"Untranslated keys: [{string.Join(", ", identical)}]");
    }

    private static HashSet<string> Placeholders(string value) =>
        [.. Regex.Matches(value, @"\{\d+\}").Select(match => match.Value)];

    private static IReadOnlyDictionary<string, string> Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", fileName);
        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }
}
