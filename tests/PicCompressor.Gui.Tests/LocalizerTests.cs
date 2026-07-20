using System.Globalization;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class LocalizerTests : IDisposable
{
    private readonly AppLanguage previous = Localizer.Instance.Language;

    public void Dispose() => Localizer.Instance.Language = previous;

    [Fact]
    public void Switching_language_changes_resolved_text_without_restart()
    {
        Localizer.Instance.Language = AppLanguage.English;
        var english = Localizer.Instance["Nav_Workspace"];

        Localizer.Instance.Language = AppLanguage.German;
        var german = Localizer.Instance["Nav_Workspace"];

        Assert.Equal("Workspace", english);
        Assert.Equal("Arbeitsbereich", german);
    }

    [Fact]
    public void Bound_strings_are_shared_per_key_and_refresh_on_switch()
    {
        Localizer.Instance.Language = AppLanguage.English;
        var bound = Localizer.Instance.GetBound("Nav_History");
        Assert.Same(bound, Localizer.Instance.GetBound("Nav_History"));

        var raised = 0;
        bound.PropertyChanged += (_, _) => raised++;

        Localizer.Instance.Language = AppLanguage.German;

        Assert.True(raised > 0);
        Assert.Equal("Verlauf", bound.Value);
    }

    [Fact]
    public void View_models_re_announce_their_derived_text_on_switch()
    {
        var settings = new SettingsViewModel();
        Localizer.Instance.Language = AppLanguage.English;
        var before = settings.EngineDescription;

        var raised = false;
        settings.PropertyChanged += (_, _) => raised = true;
        Localizer.Instance.Language = AppLanguage.German;

        Assert.True(raised);
        Assert.NotEqual(before, settings.EngineDescription);
    }

    [Fact]
    public void Culture_follows_the_selected_language()
    {
        Localizer.Instance.Language = AppLanguage.German;

        Assert.Equal("de", Localizer.Instance.Culture.TwoLetterISOLanguageName);
        Assert.Equal("de", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void System_language_falls_back_to_a_translated_language()
    {
        Localizer.Instance.Language = AppLanguage.System;

        var resolved = Localizer.Instance.Culture.TwoLetterISOLanguageName;

        Assert.Contains(resolved, new[] { "en", "de" });
    }

    [Fact]
    public void Unknown_keys_return_the_key_rather_than_throwing()
    {
        Assert.Equal("Missing_Key_Xyz", Localizer.Instance["Missing_Key_Xyz"]);
    }

    [Fact]
    public void Format_uses_the_active_culture()
    {
        Localizer.Instance.Language = AppLanguage.English;
        var text = Localizer.Instance.Format("Status_Completed", 1, 2);

        Assert.Equal("1 / 2 completed", text);
    }
}

public sealed class AppearanceViewModelTests : IDisposable
{
    private readonly AppLanguage previous = Localizer.Instance.Language;

    public void Dispose() => Localizer.Instance.Language = previous;

    [Fact]
    public void Default_theme_is_system()
    {
        var appearance = new AppearanceViewModel();

        Assert.Equal(AppTheme.System, appearance.Theme);
        Assert.True(appearance.ThemeSystem);
    }

    [Fact]
    public void Theme_selection_is_mutually_exclusive()
    {
        var appearance = new AppearanceViewModel { ThemeDark = true };

        Assert.True(appearance.ThemeDark);
        Assert.False(appearance.ThemeLight);
        Assert.False(appearance.ThemeSystem);
    }

    [Fact]
    public void Language_selection_reaches_the_localizer()
    {
        var appearance = new AppearanceViewModel { LanguageGerman = true };

        Assert.Equal(AppLanguage.German, Localizer.Instance.Language);
        Assert.True(appearance.LanguageGerman);
        Assert.False(appearance.LanguageEnglish);
    }
}
