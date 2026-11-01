using System.Net;
using System.Text.Json;
using ComponentCatalogCore.Diagnostics;
using ComponentCatalogCore;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Storage;
using ComponentCatalogCore.Taxonomy;
using ComponentCatalogCore.Transport;

namespace ComponentCatalogWinUI.Services;

public sealed class CatalogWorkspaceService : ICatalogRestoreLifecycle
{
    private static readonly Lazy<CatalogWorkspaceService> LazyCurrent = new(() => new CatalogWorkspaceService());
    public const int FixedApiPort = CatalogApiBindingOptions.DefaultPort;
    public const int FixedDiscoveryPort = MobileDiscoveryProtocol.DefaultPort;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private CatalogRepository _repository;
    private EventHandler? _catalogChangedHandler;
    private bool _isInitialized;
    private bool _isShutdown;
    private string? _apiStartupError;

    private CatalogWorkspaceService()
    {
        Options = CatalogStoragePaths.CreateRepositoryOptions();
        _repository = new CatalogRepository(Options);
        BackupService = new CatalogBackupService(new CatalogBackupOptions(
            Options.DatabasePath,
            Options.ExportPath,
            CatalogStoragePaths.BackupDirectory,
            ProductVersion.ProductVersionName,
            ComponentCategoryTaxonomy.Version));
        CatalogRuntimeState.Current.UpdatePaths(Options.DatabasePath, Options.ExportPath);
    }

    public static CatalogWorkspaceService Current => LazyCurrent.Value;
    public CatalogRepositoryOptions Options { get; }
    public CatalogRepository Repository => _repository;
    public CatalogBackupService BackupService { get; }
    public CatalogApiServer? ApiServer { get; private set; }
    public MobileDiscoveryServer? DiscoveryServer { get; private set; }
    public string? ApiStartupError => _apiStartupError;

    public async Task EnsureInitializedAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            ThrowIfShutdown();
            if (!_isInitialized)
            {
                await EnsureRepositoryInitializedCoreAsync();
            }

            if (ApiServer is null)
            {
                await StartConfiguredApiWithFallbackCoreAsync(CatalogStoragePaths.ReadLanSettings());
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task StopApiAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            await StopApiCoreAsync();
            _apiStartupError = null;
            CatalogRuntimeState.Current.UpdateApiEndpoints(null, null, "接口已停止");
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task RestartApiAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            ThrowIfShutdown();
            if (!_isInitialized)
            {
                await EnsureRepositoryInitializedCoreAsync();
            }

            await StopApiCoreAsync();
            await StartConfiguredApiWithFallbackCoreAsync(CatalogStoragePaths.ReadLanSettings());
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task ApplyLanSettingsAsync(CatalogLanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _initializationLock.WaitAsync();
        try
        {
            ThrowIfShutdown();
            if (!_isInitialized)
            {
                await EnsureRepositoryInitializedCoreAsync();
            }

            var availableAddresses = RefreshAvailableAddresses(settings.SelectedAddress);
            if (settings.LanEnabled && !availableAddresses.Any(address => string.Equals(address.Address, settings.SelectedAddress, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("所选局域网地址已失效。请选择当前可用的私有 IPv4 地址。");
            }

            await StopApiCoreAsync();
            try
            {
                await StartApiCoreAsync(settings);
                CatalogStoragePaths.SaveLanSettings(settings);
            }
            catch (Exception exception) when (settings.LanEnabled)
            {
                AppLogger.WriteStartup($"Api.LanStart.Error {exception.Message}");
                try
                {
                    var disabledSettings = new CatalogLanSettings(CatalogLanSettings.CurrentSchemaVersion, false, settings.SelectedAddress);
                    await StartApiCoreAsync(disabledSettings, "手机访问启动失败；已恢复仅本机访问。请检查高级诊断中的 URL ACL 和 Windows 防火墙。");
                    CatalogStoragePaths.SaveLanSettings(disabledSettings);
                }
                catch (Exception recoveryException)
                {
                    _apiStartupError = recoveryException.Message;
                    CatalogRuntimeState.Current.UpdateApiUnavailable($"局域网启动失败且本机接口恢复失败：{recoveryException.Message}");
                }

                throw new InvalidOperationException($"局域网接口启动失败：{exception.Message}", exception);
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task RefreshAndroidReleaseStatusAsync()
    {
        if (!File.Exists(CatalogStoragePaths.AndroidReleaseManifestPath) || !File.Exists(CatalogStoragePaths.AndroidReleaseApkPath))
        {
            CatalogRuntimeState.Current.UpdateAndroidReleaseStatus(
                $"未找到已验证的安装包：{CatalogStoragePaths.AndroidReleaseApkPath}",
                "未发布",
                "未检查",
                "未知");
            return;
        }

        try
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(CatalogStoragePaths.AndroidReleaseManifestPath));
            var root = manifest.RootElement;
            var version = root.GetProperty("versionName").GetString() ?? "未知";
            var code = root.GetProperty("versionCode").GetInt32();
            var size = root.GetProperty("sizeBytes").GetInt64();
            var sha256 = root.GetProperty("sha256").GetString() ?? "未知";
            var certificate = root.TryGetProperty("certificateSha256", out var certificateElement)
                ? certificateElement.GetString() ?? "未知"
                : "发布清单未提供";
            var publishedAt = File.GetLastWriteTimeUtc(CatalogStoragePaths.AndroidReleaseManifestPath).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            CatalogRuntimeState.Current.UpdateAndroidReleaseStatus(
                $"已发布 v{version}（versionCode {code}）· {size:N0} 字节 · SHA-256 {sha256}",
                $"v{version}（code {code}）",
                $"APK {size:N0} 字节 · SHA-256 {sha256} · 证书 SHA-256 {certificate}",
                publishedAt);
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            CatalogRuntimeState.Current.UpdateAndroidReleaseStatus(
                $"Android 发布清单无效：{exception.Message}",
                "无法读取",
                "未验证",
                "未知");
        }
    }

    public Task<CatalogDiagnosticExport> ExportDiagnosticsAsync()
    {
        var state = CatalogRuntimeState.Current;
        var report = new CatalogDiagnosticReport(
            state.ProductVersionText,
            state.TaxonomyVersionText,
            state.LocalApiUrl,
            state.LanApiUrl,
            state.ApiStatusText,
            state.PhoneSyncStatusText,
            $"{state.AndroidReleaseVersionText}; {state.AndroidReleaseIntegrityText}; published {state.AndroidReleasePublishedAtText}",
            state.BackupStatusText);
        return new CatalogDiagnosticExportService().ExportAsync(
            new CatalogDiagnosticExportOptions(Path.Combine(AppLogger.LogDirectory, "Diagnostics"), AppLogger.LogDirectory),
            report);
    }

    public async Task<CatalogBackupInfo> CreateBackupAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            ThrowIfShutdown();
            if (!_isInitialized)
            {
                await EnsureRepositoryInitializedCoreAsync();
            }

            await Repository.ExportSnapshotAsync();
            var backup = await BackupService.CreateAsync();
            CatalogRuntimeState.Current.UpdateBackupStatus(backup.RetentionWarning is null
                ? $"最新备份有效：{Path.GetFileName(backup.DirectoryPath)}"
                : $"最新备份有效，但清理警告：{backup.RetentionWarning}");
            return backup;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<CatalogBackupInfo>> ListBackupsAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            ThrowIfShutdown();
            var backups = await BackupService.ListAsync();
            CatalogRuntimeState.Current.UpdateBackupStatus(backups.Count == 0
                ? "尚无可用备份"
                : $"{backups.Count} 个备份；最新：{Path.GetFileName(backups[0].DirectoryPath)}（{(backups[0].Validation.IsValid ? "有效" : "无效")}）");
            return backups;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<CatalogRestoreResult> RestoreBackupAsync(string backupDirectoryPath)
    {
        await _initializationLock.WaitAsync();
        try
        {
            ThrowIfShutdown();
            if (!_isInitialized)
            {
                await EnsureRepositoryInitializedCoreAsync();
            }

            await Repository.ExportSnapshotAsync();
            return await BackupService.RestoreAsync(backupDirectoryPath, this);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        await _initializationLock.WaitAsync();
        try
        {
            if (_isShutdown)
            {
                return;
            }

            await StopApiCoreAsync();
            await Repository.DisposeAsync();
            _isInitialized = false;
            _isShutdown = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    Task ICatalogRestoreLifecycle.StopAsync() => StopForRestoreCoreAsync();

    Task ICatalogRestoreLifecycle.ReinitializeAsync() => ReinitializeAfterRestoreCoreAsync();

    Task ICatalogRestoreLifecycle.RegenerateSnapshotAsync() => Repository.ExportSnapshotAsync();

    private async Task StopForRestoreCoreAsync()
    {
        await StopApiCoreAsync();
        await Repository.DisposeAsync();
        _isInitialized = false;
        _apiStartupError = null;
        CatalogRuntimeState.Current.UpdateApiEndpoints(null, null, "正在恢复目录备份");
    }

    private async Task ReinitializeAfterRestoreCoreAsync()
    {
        _repository = new CatalogRepository(Options);
        await EnsureRepositoryInitializedCoreAsync();
        await StartConfiguredApiWithFallbackCoreAsync(CatalogStoragePaths.ReadLanSettings());
    }

    private async Task EnsureRepositoryInitializedCoreAsync()
    {
        var migrationResult = await CatalogStoragePaths.EnsureStorageLayoutAndMigrateAsync();
        if (!migrationResult.Succeeded)
        {
            var message = $"数据迁移失败，源数据保持不变。请修复后在设置中重试。{migrationResult.Error}";
            AppLogger.WriteStartup($"StorageMigration.Error {message}");
            CatalogRuntimeState.Current.UpdateApiUnavailable(message);
            throw new InvalidOperationException(message);
        }

        await Repository.InitializeAsync();
        _isInitialized = true;
    }

    private async Task StartConfiguredApiWithFallbackCoreAsync(CatalogLanSettings settings)
    {
        try
        {
            await StartApiCoreAsync(settings);
        }
        catch (Exception exception) when (settings.LanEnabled)
        {
            AppLogger.WriteStartup($"Api.LanStart.Error {exception.Message}");
            var disabledSettings = new CatalogLanSettings(CatalogLanSettings.CurrentSchemaVersion, false, settings.SelectedAddress);
            await StartApiCoreAsync(disabledSettings, "手机访问启动失败；已恢复仅本机访问。请检查高级诊断中的 URL ACL 和 Windows 防火墙。");
            CatalogStoragePaths.SaveLanSettings(disabledSettings);
        }
        catch (Exception exception)
        {
            _apiStartupError = exception.Message;
            CatalogRuntimeState.Current.UpdateApiUnavailable($"本地 API 启动失败：{exception.Message}");
        }
    }

    private async Task StartApiCoreAsync(CatalogLanSettings settings, string? statusDetail = null)
    {
        var availableAddresses = RefreshAvailableAddresses(settings.SelectedAddress);
        var canUseLan = settings.LanEnabled && availableAddresses.Any(address => string.Equals(address.Address, settings.SelectedAddress, StringComparison.Ordinal));
        if (settings.LanEnabled && !canUseLan)
        {
            throw new InvalidOperationException("所选局域网地址已失效。请选择当前可用的私有 IPv4 地址。");
        }

        var bindingOptions = new CatalogApiBindingOptions(FixedApiPort, canUseLan, canUseLan ? settings.SelectedAddress : null);
        var token = CatalogStoragePaths.GetOrCreateApiToken();
        CatalogApiServer? apiServer = null;
        MobileDiscoveryServer? discoveryServer = null;
        EventHandler? catalogChangedHandler = null;
        try
        {
            apiServer = await CatalogApiServer.StartAsync(
                Repository,
                token,
                bindingOptions,
                androidAppRelease: CatalogStoragePaths.CreateAndroidAppRelease());
            catalogChangedHandler = (_, _) => CatalogRuntimeState.Current.NotifyCatalogChanged();
            apiServer.CatalogChanged += catalogChangedHandler;

            if (canUseLan)
            {
                discoveryServer = await MobileDiscoveryServer.StartAsync(IPAddress.Parse(settings.SelectedAddress!), FixedApiPort, FixedDiscoveryPort);
            }

            _catalogChangedHandler = catalogChangedHandler;
            ApiServer = apiServer;
            DiscoveryServer = discoveryServer;
            _apiStartupError = null;
            CatalogRuntimeState.Current.UpdateApiEndpoints(
                apiServer.BaseUrl,
                bindingOptions.MobileSnapshotUrl,
                statusDetail);
            await RefreshAndroidReleaseStatusAsync();
        }
        catch
        {
            if (apiServer is not null && catalogChangedHandler is not null)
            {
                apiServer.CatalogChanged -= catalogChangedHandler;
            }

            if (discoveryServer is not null)
            {
                await discoveryServer.DisposeAsync();
            }

            if (apiServer is not null)
            {
                await apiServer.DisposeAsync();
            }

            throw;
        }
    }

    private async Task StopApiCoreAsync()
    {
        if (DiscoveryServer is not null)
        {
            await DiscoveryServer.DisposeAsync();
            DiscoveryServer = null;
        }

        if (ApiServer is null)
        {
            return;
        }

        if (_catalogChangedHandler is not null)
        {
            ApiServer.CatalogChanged -= _catalogChangedHandler;
            _catalogChangedHandler = null;
        }

        await ApiServer.DisposeAsync();
        ApiServer = null;
    }

    private static IReadOnlyList<CatalogNetworkAddress> RefreshAvailableAddresses(string? selectedAddress)
    {
        var addresses = CatalogNetworkAddressProvider.GetAvailablePrivateIpv4Addresses();
        CatalogRuntimeState.Current.UpdateAvailableLanAddresses(addresses, selectedAddress);
        return addresses;
    }

    private void ThrowIfShutdown()
    {
        if (_isShutdown)
        {
            throw new ObjectDisposedException(nameof(CatalogWorkspaceService));
        }
    }
}
