using System.IO;
using System.Text.Json;

namespace Youtubrowser.Settings;

public static class SettingsService
{
    public static string WebView2UserDataFolder
    {
        get
        {
            AppStoragePaths.MigrateFromLegacyAppData();
            return AppStoragePaths.WebView2UserDataFolder;
        }
    }

    public static AppSettings Load()
    {
        AppStoragePaths.MigrateFromLegacyAppData();

        try
        {
            if (File.Exists(AppStoragePaths.SettingsFile))
            {
                var json = File.ReadAllText(AppStoragePaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch (Exception)
        {
            // 設定ファイルが壊れている場合は既定値にフォールバックする
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppStoragePaths.AppDataDir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppStoragePaths.SettingsFile, json);
    }
}
