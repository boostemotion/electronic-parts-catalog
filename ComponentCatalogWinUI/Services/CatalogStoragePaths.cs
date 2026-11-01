using System.Security.Cryptography;
using System.Text.Json;
using ComponentCatalogCore;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Storage;
using ComponentCatalogCore.Transport;

namespace ComponentCatalogWinUI.Services;

public static class CatalogStoragePaths
{
    private const string AppFolderName = "ComponentCatalogWinUI";
    private const string ProjectDataFolderName = "ComponentCatalogData";
    private const string DataFolderName = "Data";
    private const string LogFolderName = "Logs";
    private const string BackupFolderName = "Backups";
    private const string DatabaseFileName = "catalog.db";
    private const string SnapshotFileName = "catalog.json";
    private const string ApiTokenFileName = "api.token";
    private const string LanSettingsFileName = "lan-settings.json";
    private const string RootOverrideVariable = "COMPONENT_CATALOG_ROOT";
    private static readonly JsonSerializerOptions LanSettingsJsonOptions = new() { WriteIndented = true };

    public static string RootDirectory => ResolveRootDirectory();
    public static string DataDirectory => Path.Combine(RootDirectory, DataFolderName);
    public static string LogDirectory => Path.Combine(RootDirectory, LogFolderName);
    public static string BackupDirectory => Path.Combine(RootDirectory, BackupFolderName);
    public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
    public static string SnapshotPath => Path.Combine(DataDirectory, SnapshotFileName);
    public static string ApiTokenPath => Path.Combine(RootDirectory, ApiTokenFileName);
    public static string LanSettingsPath => Path.Combine(RootDirectory, LanSettingsFileName);
    public static string AndroidReleaseDirectory => Path.Combine(ResolveProjectRoot(), "发布包", "Android");
    public static string AndroidReleaseApkPath => Path.Combine(AndroidReleaseDirectory, "app-release.apk");
    public static string AndroidReleaseManifestPath => Path.Combine(AndroidReleaseDirectory, "app-update.json");
    public static string AndroidReleasePublishScriptPath => Path.Combine(ResolveProjectRoot(), "发布包", "更新安卓安装包到发布包.bat");

    public static AndroidAppRelease CreateAndroidAppRelease() => new(
        AndroidReleaseApkPath,
        "com.partshelf.catalog",
        ProductVersion.AndroidVersionCode,
        ProductVersion.ProductVersionName,
        "版本统一、筛选界面优化与自动更新",
        1);

    public static void EnsureAndroidReleaseDirectory() => Directory.CreateDirectory(AndroidReleaseDirectory);

    public static CatalogRepositoryOptions CreateRepositoryOptions()
    {
        EnsureStorageLayout();
        return new CatalogRepositoryOptions(DatabasePath, SnapshotPath);
    }

    public static void EnsureStorageLayout()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    public static async Task<CatalogStorageMigrationResult> EnsureStorageLayoutAndMigrateAsync()
    {
        EnsureStorageLayout();

        foreach (var candidate in GetMigrationCandidateDirectories())
        {
            var databaseResult = await CatalogStorageMigration.MigrateDatabaseAsync(Path.Combine(candidate, DatabaseFileName), DatabasePath);
            if (!databaseResult.Succeeded)
            {
                return databaseResult;
            }

            var snapshotResult = await CatalogStorageMigration.MigrateSnapshotAsync(Path.Combine(candidate, SnapshotFileName), SnapshotPath);
            if (!snapshotResult.Succeeded)
            {
                return snapshotResult;
            }
        }

        return CatalogStorageMigrationResult.NotNeeded();
    }

    public static CatalogLanSettings ReadLanSettings()
    {
        EnsureStorageLayout();
        try
        {
            if (!File.Exists(LanSettingsPath))
            {
                return CatalogLanSettings.Disabled;
            }

            var settings = JsonSerializer.Deserialize<CatalogLanSettings>(File.ReadAllText(LanSettingsPath), LanSettingsJsonOptions);
            if (settings is null ||
                settings.SchemaVersion is < CatalogLanSettings.OldestSupportedSchemaVersion or > CatalogLanSettings.CurrentSchemaVersion ||
                (!string.IsNullOrWhiteSpace(settings.SelectedAddress) && !CatalogApiBindingOptions.IsPrivateIpv4(settings.SelectedAddress)) ||
                !CatalogNetworkPorts.IsValidPair(settings.ApiPort, settings.DiscoveryPort))
            {
                return CatalogLanSettings.Disabled;
            }

            return settings with { SchemaVersion = CatalogLanSettings.CurrentSchemaVersion };
        }
        catch (IOException)
        {
            return CatalogLanSettings.Disabled;
        }
        catch (JsonException)
        {
            return CatalogLanSettings.Disabled;
        }
        catch (UnauthorizedAccessException)
        {
            return CatalogLanSettings.Disabled;
        }
    }

    public static void SaveLanSettings(CatalogLanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != CatalogLanSettings.CurrentSchemaVersion ||
            (!string.IsNullOrWhiteSpace(settings.SelectedAddress) && !CatalogApiBindingOptions.IsPrivateIpv4(settings.SelectedAddress)) ||
            (settings.LanEnabled && string.IsNullOrWhiteSpace(settings.SelectedAddress)) ||
            !CatalogNetworkPorts.IsValidPair(settings.ApiPort, settings.DiscoveryPort))
        {
            throw new ArgumentException("LAN settings are invalid.", nameof(settings));
        }

        EnsureStorageLayout();
        WriteAtomically(LanSettingsPath, JsonSerializer.Serialize(settings, LanSettingsJsonOptions));
    }

    public static string GetOrCreateApiToken()
    {
        EnsureStorageLayout();
        if (File.Exists(ApiTokenPath))
        {
            var token = File.ReadAllText(ApiTokenPath).Trim();
            if (IsValidApiToken(token))
            {
                return token;
            }
        }

        return WriteApiToken(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public static string RotateApiToken()
    {
        return WriteApiToken(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    private static string WriteApiToken(string token)
    {
        EnsureStorageLayout();
        WriteAtomically(ApiTokenPath, token);
        File.SetAttributes(ApiTokenPath, File.GetAttributes(ApiTokenPath) | FileAttributes.Hidden);
        return token;
    }

    private static bool IsValidApiToken(string token)
    {
        return token.Length == 64 && token.All(Uri.IsHexDigit);
    }

    private static void WriteAtomically(string destinationPath, string content)
    {
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static IReadOnlyList<string> GetMigrationCandidateDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return CatalogStoragePathResolver.GetMigrationCandidateDirectories(localAppData, AppFolderName, ResolveProjectRoot());
    }

    private static string ResolveRootDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot.Trim());
        }

        return Path.Combine(ResolveProjectRoot(), ProjectDataFolderName);
    }

    private static string ResolveProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ComponentCatalog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
