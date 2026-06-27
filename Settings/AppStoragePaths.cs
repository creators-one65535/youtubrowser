using System.IO;

namespace Youtubrowser.Settings;

internal static class AppStoragePaths
{
    private const string AppDataFolderName = "Youtubrowser";
    private const string LegacyAppDataFolderName = "YouTubeBrowser";

    private static readonly object MigrationLock = new();
    private static bool _migrationAttempted;

    private static readonly string LocalAppDataDir = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);

    private static readonly string LegacyAppDataDir = Path.Combine(
        LocalAppDataDir,
        LegacyAppDataFolderName);

    private static readonly string LegacySettingsFile = Path.Combine(LegacyAppDataDir, "settings.json");
    private static readonly string LegacyHistoryFile = Path.Combine(LegacyAppDataDir, "history.json");
    private static readonly string LegacyWebView2UserDataFolder = Path.Combine(LegacyAppDataDir, "WebView2UserData");

    public static string AppDataDir { get; } = Path.Combine(LocalAppDataDir, AppDataFolderName);
    public static string SettingsFile { get; } = Path.Combine(AppDataDir, "settings.json");
    public static string HistoryFile { get; } = Path.Combine(AppDataDir, "history.json");
    public static string WebView2UserDataFolder { get; } = Path.Combine(AppDataDir, "WebView2UserData");

    public static void MigrateFromLegacyAppData()
    {
        lock (MigrationLock)
        {
            if (_migrationAttempted) return;
            _migrationAttempted = true;

            TryMigrate(() => CopyFileIfMissing(LegacySettingsFile, SettingsFile));
            TryMigrate(() => CopyFileIfMissing(LegacyHistoryFile, HistoryFile));
            TryMigrate(() => CopyDirectoryIfMissing(LegacyWebView2UserDataFolder, WebView2UserDataFolder));
        }
    }

    private static void TryMigrate(Action migrate)
    {
        try
        {
            migrate();
        }
        catch (Exception)
        {
            // 旧名フォルダの移行に失敗しても、アプリの起動は止めない。
        }
    }

    private static void CopyFileIfMissing(string sourceFile, string destinationFile)
    {
        if (!File.Exists(sourceFile) || File.Exists(destinationFile)) return;

        var destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (destinationDirectory is not null) Directory.CreateDirectory(destinationDirectory);

        File.Copy(sourceFile, destinationFile, overwrite: false);
    }

    private static void CopyDirectoryIfMissing(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory) || Directory.Exists(destinationDirectory)) return;

        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            var destinationFileDirectory = Path.GetDirectoryName(destinationFile);
            if (destinationFileDirectory is not null) Directory.CreateDirectory(destinationFileDirectory);
            File.Copy(file, destinationFile, overwrite: false);
        }
    }
}
