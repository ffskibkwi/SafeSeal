using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SafeSeal.App.Services;

namespace SafeSeal.App.Dialogs;

public partial class SettingsDialog : Window, INotifyPropertyChanged
{
    private readonly LocalizationService _localization;
    private readonly ThemeService _themeService;

    public SettingsDialog()
    {
        _localization = LocalizationService.Instance;
        _themeService = ThemeService.Instance;

        string language = _localization.CurrentLanguage;
        IsEnglish = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        IsChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        IsJapanese = language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

        if (!IsEnglish && !IsChinese && !IsJapanese)
        {
            IsEnglish = true;
        }

        AppTheme selectedTheme = _themeService.CurrentTheme;
        IsThemeSystem = selectedTheme == AppTheme.System;
        IsThemeLight = selectedTheme == AppTheme.Light;
        IsThemeDark = selectedTheme == AppTheme.Dark;

        _localization.LanguageChanged += OnLanguageChanged;

        CloseCommand = new RelayCommand(() => DialogResult = false);
        SaveCommand = new RelayCommand(Save);

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand SaveCommand { get; }

    public bool IsEnglish { get; set; }

    public bool IsChinese { get; set; }

    public bool IsJapanese { get; set; }

    public bool IsThemeSystem { get; set; }

    public bool IsThemeLight { get; set; }

    public bool IsThemeDark { get; set; }

    public string SettingsTitleText => _localization["SettingsTitle"];

    public string LanguageText => _localization["Language"];

    public string ThemeText => _localization["Theme"];

    public string ThemeSystemText => _localization["ThemeSystem"];

    public string ThemeLightText => _localization["ThemeLight"];

    public string ThemeDarkText => _localization["ThemeDark"];

    public string SaveText => _localization["Save"];

    public string CancelText => _localization["Cancel"];

    public string LanguageEnglishText => _localization["LanguageEnglish"];

    public string LanguageChineseText => _localization["LanguageChinese"];

    public string LanguageJapaneseText => _localization["LanguageJapanese"];

    public string BrandingVersionText => _localization["SettingsVersion"];

    public string BrandingCopyrightText => _localization["SettingsCopyright"];

    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private void Save()
    {
        string language = IsJapanese
            ? "ja-JP"
            : IsChinese
                ? "zh-CN"
                : "en-US";

        _localization.SetLanguage(language);

        AppTheme selectedTheme = IsThemeDark
            ? AppTheme.Dark
            : IsThemeLight
                ? AppTheme.Light
                : AppTheme.System;

        _themeService.ApplyTheme(selectedTheme, Application.Current);

        DialogResult = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SettingsTitleText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(ThemeText));
        OnPropertyChanged(nameof(ThemeSystemText));
        OnPropertyChanged(nameof(ThemeLightText));
        OnPropertyChanged(nameof(ThemeDarkText));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(LanguageEnglishText));
        OnPropertyChanged(nameof(LanguageChineseText));
        OnPropertyChanged(nameof(LanguageJapaneseText));
        OnPropertyChanged(nameof(BrandingVersionText));
        OnPropertyChanged(nameof(BrandingCopyrightText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}
