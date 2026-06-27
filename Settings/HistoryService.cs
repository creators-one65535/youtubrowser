using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Youtubrowser.Settings;

public static class HistoryService
{
    public static List<HistoryEntry> Load()
    {
        AppStoragePaths.MigrateFromLegacyAppData();

        try
        {
            if (File.Exists(AppStoragePaths.HistoryFile))
            {
                var json = File.ReadAllText(AppStoragePaths.HistoryFile);
                var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
                if (entries is not null) return entries;
            }
        }
        catch (Exception)
        {
            // 履歴ファイルが壊れている場合は空の履歴にフォールバックする
        }
        return new List<HistoryEntry>();
    }

    public static void Save(List<HistoryEntry> entries)
    {
        Directory.CreateDirectory(AppStoragePaths.AppDataDir);
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppStoragePaths.HistoryFile, json);
    }
}
