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

    public AppPreferenceStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSeal");

        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "preferences.json");
    }

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppPreferences();
            }

            string json = File.ReadAllText(_settingsPath);
            AppPreferences? parsed = JsonSerializer.Deserialize<AppPreferences>(json, SerializerOptions);
            return parsed ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        string json = JsonSerializer.Serialize(preferences, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
