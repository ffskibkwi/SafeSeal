using System.Windows;
using Microsoft.Win32;

namespace SafeSeal.App.Services;

public sealed class ThemeService
{
    private readonly AppPreferenceStore _store;

    public ThemeService(AppPreferenceStore store)
    {
        _store = store;
    }

    public static ThemeService Instance { get; } = new(new AppPreferenceStore());

    public event EventHandler? ThemeChanged;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public void Initialize(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        AppPreferences preferences = _store.Load();
        ApplyTheme(preferences.Theme, application, persist: false);
    }

    public void ApplyTheme(AppTheme theme, Application application, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(application);

        AppTheme effectiveTheme = theme == AppTheme.System
            ? DetectSystemTheme()
            : theme;

        var dictionaries = application.Resources.MergedDictionaries;
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            Uri? source = dictionaries[i].Source;
            if (source is null)
            {
                continue;
            }

            if (source.OriginalString.Contains("Themes/Theme.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        string sourcePath = effectiveTheme == AppTheme.Dark
            ? "Themes/Theme.Dark.xaml"
            : "Themes/Theme.Light.xaml";

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(sourcePath, UriKind.Relative),
        });

        CurrentTheme = theme;

        if (persist)
        {
            AppPreferences current = _store.Load();
            _store.Save(current with { Theme = theme });
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static AppTheme DetectSystemTheme()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);

            if (value is int intValue && intValue == 0)
            {
                return AppTheme.Dark;
            }
        }
        catch
        {
            // Fallback to light.
        }

        return AppTheme.Light;
    }
}

