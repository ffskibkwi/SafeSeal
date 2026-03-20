using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SafeSeal.App.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly AppPreferenceStore _store;
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public LocalizationService(AppPreferenceStore store)
    {
        _store = store;
        _resourceManager = new ResourceManager("SafeSeal.App.Resources.Strings", typeof(LocalizationService).Assembly);
        _currentCulture = CultureInfo.CurrentUICulture;

        SupportedLanguages =
        [
            new LanguageOption("en-US", "English"),
            new LanguageOption("zh-CN", "简体中文"),
            new LanguageOption("ja-JP", "日本語"),
        ];
    }

    public static LocalizationService Instance { get; } = new(new AppPreferenceStore());

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    public string CurrentLanguage => _currentCulture.Name;

    public string this[string key] => GetString(key);

    public void Initialize()
    {
        AppPreferences preferences = _store.Load();
        SetLanguage(preferences.Language, persist: false);
    }

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string? localized = _resourceManager.GetString(key, _currentCulture);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        string? fallback = _resourceManager.GetString(key, CultureInfo.GetCultureInfo("en-US"));
        return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
    }

    public void SetLanguage(string cultureName, bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            cultureName = "en-US";
        }

        CultureInfo target;
        try
        {
            target = CultureInfo.GetCultureInfo(cultureName);
        }
        catch
        {
            target = CultureInfo.GetCultureInfo("en-US");
        }

        if (string.Equals(_currentCulture.Name, target.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentCulture = target;
        CultureInfo.DefaultThreadCurrentCulture = target;
        CultureInfo.DefaultThreadCurrentUICulture = target;
        Thread.CurrentThread.CurrentCulture = target;
        Thread.CurrentThread.CurrentUICulture = target;

        if (persist)
        {
            AppPreferences current = _store.Load();
            _store.Save(current with { Language = target.Name });
        }

        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
