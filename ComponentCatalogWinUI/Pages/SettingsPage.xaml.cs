// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using ComponentCatalogWinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ComponentCatalogWinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private CatalogRuntimeState RuntimeState => CatalogRuntimeState.Current;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = RuntimeState;
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
        if (string.IsNullOrWhiteSpace(RuntimeState.ApiBaseUrl))
        {
            SettingsActionStatusText.Text = "API 尚未启动，无法复制地址。";
            return;
        }

        CopyText(RuntimeState.ApiBaseUrl, "API 地址");
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
            AppLogger.WritePage($"Settings.OpenDirectory.Error {displayName} {directory} {ex}");
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
            AppLogger.WritePage($"Settings.CopyText.Error {displayName} {ex}");
            SettingsActionStatusText.Text = $"复制{displayName}失败：{ex.Message}";
        }
    }
}
