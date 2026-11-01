// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ComponentCatalogCore.Storage;
using ComponentCatalogWinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ComponentCatalogWinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private CatalogRuntimeState RuntimeState => CatalogRuntimeState.Current;
    private CatalogWorkspaceService WorkspaceService => CatalogWorkspaceService.Current;
    private bool _loadingLanControls;

    public ObservableCollection<CatalogBackupListItem> Backups { get; } = [];

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = RuntimeState;
        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLanControls();
        await RefreshAndroidReleaseStatusAsync();
        await RefreshBackupsAsync();
    }

    private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var backup = await WorkspaceService.CreateBackupAsync();
            await RefreshBackupsAsync();
            SettingsActionStatusText.Text = backup.RetentionWarning is null
                ? $"已创建备份：{Path.GetFileName(backup.DirectoryPath)}（{backup.Metadata.ComponentCount} 个元件）。"
                : $"已创建备份：{Path.GetFileName(backup.DirectoryPath)}。备份清理警告：{backup.RetentionWarning}";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.CreateBackup.Error {ex.Message}");
            SettingsActionStatusText.Text = $"创建备份失败：{ex.Message}";
        }
    }

    private void OpenBackupDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        CatalogStoragePaths.EnsureStorageLayout();
        OpenDirectory(CatalogStoragePaths.BackupDirectory, "备份目录");
    }

    private async void RestoreSelectedBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupListView.SelectedItem is not CatalogBackupListItem selected)
        {
            SettingsActionStatusText.Text = "请先选择一个已验证的备份。";
            return;
        }

        if (!selected.Backup.Validation.IsValid)
        {
            SettingsActionStatusText.Text = $"所选备份无法恢复：{selected.Backup.Validation.Error}";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "恢复所选备份？",
            Content = $"将恢复 {selected.CreatedDisplay} 的备份（{selected.Backup.Metadata.ComponentCount} 个元件）。应用会先创建当前数据的安全备份，并暂时停止本机接口和手机发现服务。若恢复失败，会自动回滚当前数据。",
            PrimaryButtonText = "恢复备份",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var result = await WorkspaceService.RestoreBackupAsync(selected.Backup.DirectoryPath);
            await RefreshBackupsAsync();
            SettingsActionStatusText.Text = result.Error is null
                ? $"已恢复备份，并创建安全备份：{Path.GetFileName(result.SafetyBackupDirectoryPath)}。"
                : $"已恢复备份，并创建安全备份：{Path.GetFileName(result.SafetyBackupDirectoryPath)}。备份清理警告：{result.Error}";
        }
        catch (CatalogRestoreException ex)
        {
            AppLogger.WritePage($"Settings.RestoreBackup.Error Rollback={ex.RollbackSucceeded} {ex.Message}");
            SettingsActionStatusText.Text = ex.RollbackSucceeded
                ? "恢复失败，当前数据已自动回滚。"
                : $"恢复失败且自动回滚失败：{ex.Message}";
            await RefreshBackupsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.RestoreBackup.Error {ex.Message}");
            SettingsActionStatusText.Text = $"恢复备份失败：{ex.Message}";
            await RefreshBackupsAsync();
        }
    }

    private void OpenDataDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDirectory(Path.GetDirectoryName(RuntimeState.DatabasePath), "数据目录");
    }

    private void OpenSnapshotDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDirectory(Path.GetDirectoryName(RuntimeState.SnapshotPath), "快照目录");
    }

    private void OpenLogDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDirectory(RuntimeState.LogDirectory, "日志目录", createIfMissing: true);
    }

    private async void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var export = await WorkspaceService.ExportDiagnosticsAsync();
            CopyText(export.Content, "已脱敏诊断");
            SettingsActionStatusText.Text = $"已复制已脱敏诊断，并保存到：{export.ReportPath}";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.CopyDiagnostics.Error {ex.Message}");
            SettingsActionStatusText.Text = $"生成已脱敏诊断失败：{ex.Message}";
        }
    }

    private void CopyDatabasePathButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(RuntimeState.DatabasePath, "数据库路径");
    }

    private void CopySnapshotPathButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(RuntimeState.SnapshotPath, "快照路径");
    }

    private void CopyApiBaseUrlButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(RuntimeState.LocalApiUrl, "本机 API 地址");
    }

    private void CopyLanApiUrlButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(RuntimeState.LanApiUrl, "手机 API 地址");
    }

    private void CopyApiTokenButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(CatalogStoragePaths.GetOrCreateApiToken(), "访问 Token");
    }

    private async void RestartApiButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await WorkspaceService.RestartApiAsync();
            SettingsActionStatusText.Text = $"已重启接口：{RuntimeState.LocalApiUrl}";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.RestartApi.Error {ex.Message}");
            SettingsActionStatusText.Text = $"重启接口失败：{ex.Message}";
        }
    }

    private async void ApplyLanSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyPhoneAccessAsync(PhoneSyncToggle.IsOn);
    }

    private async void PhoneSyncToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadingLanControls)
        {
            await ApplyPhoneAccessAsync(PhoneSyncToggle.IsOn);
        }
    }

    private async void RestartPhoneAccessButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await WorkspaceService.RestartApiAsync();
            SettingsActionStatusText.Text = PhoneSyncToggle.IsOn ? "手机访问已重新启动。" : "本机管理接口已重新启动。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.RestartPhoneAccess.Error {ex.Message}");
            SettingsActionStatusText.Text = $"重新启动手机访问失败：{ex.Message}";
        }
    }

    private async Task ApplyPhoneAccessAsync(bool enabled)
    {
        try
        {
            var selectedAddress = (LanAddressComboBox.SelectedItem as CatalogNetworkAddress)?.Address;
            if (enabled && string.IsNullOrWhiteSpace(selectedAddress))
            {
                selectedAddress = RuntimeState.AvailableLanAddresses.FirstOrDefault()?.Address;
            }

            if (enabled && string.IsNullOrWhiteSpace(selectedAddress))
            {
                SettingsActionStatusText.Text = "未发现可用的私有 IPv4 地址，无法开启手机访问。";
                LoadLanControls();
                return;
            }

            var settings = new CatalogLanSettings(CatalogLanSettings.CurrentSchemaVersion, enabled, selectedAddress);
            await WorkspaceService.ApplyLanSettingsAsync(settings);
            LoadLanControls();
            SettingsActionStatusText.Text = enabled ? "手机只读同步已开启。" : "手机访问已关闭；本机管理接口仍在运行。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.ApplyPhoneAccess.Error {ex.Message}");
            LoadLanControls();
            SettingsActionStatusText.Text = $"应用手机访问设置失败：{ex.Message}";
        }
    }

    private async void RotateApiTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "轮换访问 Token？",
            Content = "旧的本机管理 API 配置会立即失效。手机只读同步不使用 Token。",
            PrimaryButtonText = "轮换并重启接口",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            CatalogStoragePaths.RotateApiToken();
            await WorkspaceService.RestartApiAsync();
            SettingsActionStatusText.Text = "管理 Token 已轮换，接口已重启；手机只读同步不受影响。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.RotateApiToken.Error {ex.Message}");
            SettingsActionStatusText.Text = $"Token 已轮换，但接口重启失败：{ex.Message}";
        }
    }

    private async void ReleaseApiPortButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await WorkspaceService.StopApiAsync();
            SettingsActionStatusText.Text = $"已释放 HTTP {CatalogWorkspaceService.FixedApiPort} 和 UDP {CatalogWorkspaceService.FixedDiscoveryPort} 端口。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.ReleaseApi.Error {ex.Message}");
            SettingsActionStatusText.Text = $"释放接口端口失败：{ex.Message}";
        }
    }

    private void ShowFirewallGuidanceButton_Click(object sender, RoutedEventArgs e)
    {
        var address = (LanAddressComboBox.SelectedItem as CatalogNetworkAddress)?.Address ?? "<选定私有IPv4>";
        SettingsActionStatusText.Text =
            $"若手机无法连接，请由管理员为明确地址配置 URL ACL，例如：netsh http add urlacl url=http://{address}:{CatalogWorkspaceService.FixedApiPort}/ user=<当前Windows用户>；" +
            "并在 Windows 防火墙允许此端口的专用网络入站规则。本程序不会自动执行这些操作。";
    }

    private void LoadLanControls()
    {
        var settings = CatalogStoragePaths.ReadLanSettings();
        var addresses = CatalogNetworkAddressProvider.GetAvailablePrivateIpv4Addresses();
        var selectedAddress = settings.SelectedAddress ?? addresses.FirstOrDefault()?.Address;
        RuntimeState.UpdateAvailableLanAddresses(addresses, selectedAddress);

        _loadingLanControls = true;
        try
        {
            LanAddressComboBox.ItemsSource = addresses;
            LanAddressComboBox.SelectedValue = selectedAddress;
            PhoneSyncToggle.IsOn = settings.LanEnabled;
        }
        finally
        {
            _loadingLanControls = false;
        }
    }

    private void OpenAndroidReleaseDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        CatalogStoragePaths.EnsureAndroidReleaseDirectory();
        OpenDirectory(CatalogStoragePaths.AndroidReleaseDirectory, "Android APK 发布目录");
    }

    private async void CheckAndroidReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAndroidReleaseStatusAsync();
    }

    private void RebuildAndroidReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(CatalogStoragePaths.AndroidReleasePublishScriptPath))
        {
            SettingsActionStatusText.Text = $"未找到 Android 发布脚本：{CatalogStoragePaths.AndroidReleasePublishScriptPath}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CatalogStoragePaths.AndroidReleasePublishScriptPath,
                WorkingDirectory = Path.GetDirectoryName(CatalogStoragePaths.AndroidReleasePublishScriptPath),
                UseShellExecute = true,
            });
            SettingsActionStatusText.Text = "Android 安装包生成已在单独窗口启动；完成后点击“检查发布文件”。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.RebuildAndroidRelease.Error {ex.Message}");
            SettingsActionStatusText.Text = $"启动 Android 安装包生成失败：{ex.Message}";
        }
    }

    private async Task RefreshAndroidReleaseStatusAsync()
    {
        try
        {
            await WorkspaceService.RefreshAndroidReleaseStatusAsync();
            SettingsActionStatusText.Text = RuntimeState.AndroidReleaseStatusText;
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.CheckAndroidRelease.Error {ex.Message}");
            SettingsActionStatusText.Text = $"检查 Android 发布文件失败：{ex.Message}";
        }
    }

    private async Task RefreshBackupsAsync()
    {
        try
        {
            var backups = await WorkspaceService.ListBackupsAsync();
            Backups.Clear();
            foreach (var backup in backups)
            {
                Backups.Add(CatalogBackupListItem.FromBackup(backup));
            }
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.ListBackups.Error {ex.Message}");
            SettingsActionStatusText.Text = $"读取备份列表失败：{ex.Message}";
        }
    }

    private void OpenDirectory(string? directory, string displayName, bool createIfMissing = false)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            SettingsActionStatusText.Text = $"{displayName}路径为空。";
            return;
        }

        try
        {
            if (createIfMissing)
            {
                Directory.CreateDirectory(directory);
            }

            if (!Directory.Exists(directory))
            {
                SettingsActionStatusText.Text = $"{displayName}不存在：{directory}";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
            });
            SettingsActionStatusText.Text = $"已打开{displayName}：{directory}";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.OpenDirectory.Error {displayName} {directory} {ex.Message}");
            SettingsActionStatusText.Text = $"打开{displayName}失败：{ex.Message}";
        }
    }

    private void CopyText(string? text, string displayName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SettingsActionStatusText.Text = $"{displayName}为空。";
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            SettingsActionStatusText.Text = $"已复制{displayName}。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"Settings.CopyText.Error {displayName} {ex.Message}");
            SettingsActionStatusText.Text = $"复制{displayName}失败：{ex.Message}";
        }
    }
}

public sealed class CatalogBackupListItem
{
    public CatalogBackupInfo Backup { get; set; } = null!;
    public string CreatedDisplay { get; set; } = string.Empty;
    public string ComponentCountDisplay { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = string.Empty;

    public static CatalogBackupListItem FromBackup(CatalogBackupInfo backup)
    {
        var createdDisplay = DateTimeOffset.TryParse(backup.Metadata.CreatedUtc, out var createdAt)
            ? createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "时间未知";
        return new CatalogBackupListItem
        {
            Backup = backup,
            CreatedDisplay = createdDisplay,
            ComponentCountDisplay = $"{backup.Metadata.ComponentCount} 个元件",
            StatusDisplay = !backup.Validation.IsValid
                ? "无效"
                : backup.RetentionWarning is null ? "可恢复" : "可恢复 · 清理警告",
        };
    }
}
