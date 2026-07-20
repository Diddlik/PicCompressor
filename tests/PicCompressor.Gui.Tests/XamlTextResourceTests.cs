using System.Text.RegularExpressions;

namespace PicCompressor.Gui.Tests;

/// <summary>
/// Sichtbare Texte dürfen nicht in XAML verdrahtet sein; sie kommen aus den Ressourcen
/// (agent_instructions.md, Abschnitt Lokalisierung).
/// </summary>
public sealed class XamlTextResourceTests
{
    private static readonly string[] TextAttributes =
    [
        "Text", "Content", "PlaceholderText", "Header", "ToolTip.Tip",
        "AutomationProperties.Name", "AutomationProperties.HelpText"
    ];

    public static TheoryData<string> ViewFiles
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Directory.GetFiles(
                         Path.Combine(AppContext.BaseDirectory, "Views"),
                         "*.axaml"))
            {
                data.Add(file);
            }

            return data;
        }
    }

    [Fact]
    public void View_files_are_available_to_the_check() =>
        Assert.NotEmpty(Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "Views"),
            "*.axaml"));

    [Theory]
    [MemberData(nameof(ViewFiles))]
    public void No_visible_text_is_hardcoded(string path)
    {
        var xaml = File.ReadAllText(path);
        var offenders = new List<string>();

        foreach (var attribute in TextAttributes)
        {
            var pattern = new Regex(attribute + @"=""(?<value>[^""]*)""");
            foreach (Match match in pattern.Matches(xaml))
            {
                var value = match.Groups["value"].Value;
                if (IsAcceptable(value))
                {
                    continue;
                }

                offenders.Add($"{attribute}=\"{value}\"");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{Path.GetFileName(path)} carries hardcoded text: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Technische Notation ohne Wortbestandteile, etwa das Chroma-Subsampling <c>4:2:0</c>.
    /// Sie ist ein stabiler Bezeichner und plattform- wie sprachidentisch (Abschnitt 4.3).
    /// </summary>
    private static readonly Regex TechnicalNotation = new(@"^[0-9]+(:[0-9]+)+$");

    private static bool IsAcceptable(string value)
    {
        var trimmed = value.Trim();

        // Bindungen und Markup-Erweiterungen sind der zulässige Weg.
        if (trimmed.StartsWith('{'))
        {
            return true;
        }

        // Leere Werte tragen keinen Text.
        return trimmed.Length == 0 || TechnicalNotation.IsMatch(trimmed);
    }
}
