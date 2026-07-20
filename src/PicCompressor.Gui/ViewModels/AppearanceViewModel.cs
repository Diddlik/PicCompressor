using Avalonia;
using Avalonia.Styling;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

public enum AppTheme
{
    /// <summary>Folgt der Einstellung des Betriebssystems.</summary>
    System,
    Light,
    Dark
}

/// <summary>Sprache und Design. Beide wirken sofort, ohne Neustart.</summary>
public sealed class AppearanceViewModel : ObservableObject
{
    private AppTheme theme = AppTheme.System;

    public AppLanguage Language
    {
        get => Localizer.Instance.Language;
        set
        {
            if (Localizer.Instance.Language == value)
            {
                return;
            }

            Localizer.Instance.Language = value;
            Raise(nameof(Language));
            Raise(nameof(LanguageSystem));
            Raise(nameof(LanguageEnglish));
            Raise(nameof(LanguageGerman));
        }
    }

    public bool LanguageSystem
    {
        get => Language is AppLanguage.System;
        set { if (value) { Language = AppLanguage.System; } }
    }

    public bool LanguageEnglish
    {
        get => Language is AppLanguage.English;
        set { if (value) { Language = AppLanguage.English; } }
    }

    public bool LanguageGerman
    {
        get => Language is AppLanguage.German;
        set { if (value) { Language = AppLanguage.German; } }
    }

    public AppTheme Theme
    {
        get => theme;
        set
        {
            if (!SetProperty(ref theme, value))
            {
                return;
            }

            if (Avalonia.Application.Current is { } application)
            {
                application.RequestedThemeVariant = value switch
                {
                    AppTheme.Light => ThemeVariant.Light,
                    AppTheme.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default
                };
            }

            Raise(nameof(ThemeSystem));
            Raise(nameof(ThemeLight));
            Raise(nameof(ThemeDark));
        }
    }

    public bool ThemeSystem
    {
        get => Theme is AppTheme.System;
        set { if (value) { Theme = AppTheme.System; } }
    }

    public bool ThemeLight
    {
        get => Theme is AppTheme.Light;
        set { if (value) { Theme = AppTheme.Light; } }
    }

    public bool ThemeDark
    {
        get => Theme is AppTheme.Dark;
        set { if (value) { Theme = AppTheme.Dark; } }
    }
}
