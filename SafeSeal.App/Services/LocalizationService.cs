using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SafeSeal.App.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly AppPreferenceStore _store;
    private CultureInfo _currentCulture;

    private readonly Dictionary<string, (string En, string ZhCn)> _translations = new(StringComparer.Ordinal)
    {
        ["AppTitle"] = ("SafeSeal", "SafeSeal"),
        ["AppSubtitle"] = ("Private document vault", "私密文档保险库"),
        ["Import"] = ("Import", "导入"),
        ["Settings"] = ("Settings", "设置"),
        ["About"] = ("About", "关于"),
        ["Items"] = ("Items", "项目"),
        ["SecureDocumentsFormat"] = ("{0} secure documents", "{0} 个加密文档"),
        ["MyDocuments"] = ("My Documents", "我的文档"),
        ["NoDocuments"] = ("No documents yet", "暂无文档"),
        ["NoDocumentsHint"] = ("Import a photo to create your first secure item.", "导入照片以创建第一个安全文档。"),
        ["Watermark"] = ("Watermark", "水印"),
        ["LivePreview"] = ("Live Preview", "实时预览"),
        ["Template"] = ("Template", "模板"),
        ["TextLines"] = ("Text Lines", "文本行数"),
        ["Tint"] = ("Tint", "颜色"),
        ["Opacity"] = ("Opacity", "透明度"),
        ["FontSize"] = ("Font Size", "字体大小"),
        ["HorizontalSpacing"] = ("Horizontal Spacing", "水平间距"),
        ["VerticalSpacing"] = ("Vertical Spacing", "垂直间距"),
        ["Angle"] = ("Angle", "角度"),
        ["Export"] = ("Export", "导出"),
        ["Working"] = ("Working...", "处理中..."),
        ["DeletePromptFormat"] = ("Delete '{0}' from secure vault?", "确认从安全保险库删除“{0}”？"),
        ["DeleteTitle"] = ("Delete Document", "删除文档"),
        ["Delete"] = ("Delete", "删除"),
        ["Cancel"] = ("Cancel", "取消"),
        ["AboutTitle"] = ("About SafeSeal", "关于 SafeSeal"),
        ["AboutVersion"] = ("Version v1.0.0", "版本 v1.0.0"),
        ["AboutLicense"] = ("License: MIT", "许可证：MIT"),
        ["AboutOpenRepo"] = ("Open GitHub Repository", "打开 GitHub 仓库"),
        ["Close"] = ("Close", "关闭"),
        ["SettingsTitle"] = ("Settings", "设置"),
        ["Language"] = ("Language", "语言"),
        ["Theme"] = ("Theme", "主题"),
        ["ThemeSystem"] = ("System", "跟随系统"),
        ["ThemeLight"] = ("Light", "浅色"),
        ["ThemeDark"] = ("Dark", "深色"),
        ["Save"] = ("Save", "保存"),
        ["Rename"] = ("Rename", "重命名"),
    };

    public LocalizationService(AppPreferenceStore store)
    {
        _store = store;
        _currentCulture = CultureInfo.CurrentUICulture;

        SupportedLanguages =
        [
            new LanguageOption("en-US", "English"),
            new LanguageOption("zh-CN", "简体中文"),
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
        if (!_translations.TryGetValue(key, out (string En, string ZhCn) value))
        {
            return key;
        }

        return _currentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? value.ZhCn
            : value.En;
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
            target = new CultureInfo(cultureName);
        }
        catch
        {
            target = new CultureInfo("en-US");
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
