using System.IO;
using System.Text.Json;

namespace SafeSeal.App.Services;

public sealed class AppPreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly string _legacyPreferencesPath;

    public AppPreferenceStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSeal");

        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
        _legacyPreferencesPath = Path.Combine(root, "preferences.json");
    }

    public AppPreferences Load()
    {
        if (TryLoad(_settingsPath, out AppPreferences settings))
        {
            return EnsureDefaults(settings);
        }

        if (TryLoad(_legacyPreferencesPath, out AppPreferences legacy))
        {
            AppPreferences migrated = EnsureDefaults(legacy);
            Save(migrated);
            return migrated;
        }

        return new AppPreferences();
    }

    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        AppPreferences current = TryLoad(_settingsPath, out AppPreferences existing)
            ? EnsureDefaults(existing)
            : new AppPreferences();

        AppPreferences merged = Merge(current, EnsureDefaults(preferences));
        string json = JsonSerializer.Serialize(merged, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static AppPreferences Merge(AppPreferences current, AppPreferences incoming)
    {
        string language = string.IsNullOrWhiteSpace(incoming.Language)
            ? current.Language
            : incoming.Language;

        WorkspacePreferences workspace = incoming.Workspace ?? current.Workspace ?? WorkspacePreferences.CreateDefault();

        return current with
        {
            Language = language,
            Theme = incoming.Theme,
            Workspace = workspace,
        };
    }

    private static AppPreferences EnsureDefaults(AppPreferences source)
    {
        WorkspacePreferences workspace = source.Workspace ?? WorkspacePreferences.CreateDefault();
        return source with { Workspace = workspace };
    }

    private bool TryLoad(string path, out AppPreferences preferences)
    {
        preferences = new AppPreferences();

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string json = File.ReadAllText(path);
            AppPreferences? parsed = JsonSerializer.Deserialize<AppPreferences>(json, SerializerOptions);
            if (parsed is null)
            {
                return false;
            }

            preferences = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }
}




