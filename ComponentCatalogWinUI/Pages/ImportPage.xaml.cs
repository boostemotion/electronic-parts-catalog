using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComponentCatalogCore.Persistence;
using ComponentCatalogWinUI.Services;

namespace ComponentCatalogWinUI.Pages;

public sealed partial class ImportPage : Page
{
    private readonly CatalogRepositoryOptions _repositoryOptions;

    public ImportPage()
    {
        InitializeComponent();

        _repositoryOptions = CatalogStoragePaths.CreateRepositoryOptions();
        CatalogRuntimeState.Current.UpdatePaths(_repositoryOptions.DatabasePath, _repositoryOptions.ExportPath);
    }

    private async void SelectCsvFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            
            // 桌面环境 HWND 初始化
            var mainWindow = (Application.Current as App)?.MainWindow as MainWindow;
            if (mainWindow is null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".csv");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string csvText = await Windows.Storage.FileIO.ReadTextAsync(file);
                ImportTextBox.Text = csvText;
                ImportStatusText.Text = $"已载入文件: {file.Name} ({csvText.Length} 字节)。请选择策略完成导入。";
            }
        }
        catch (Exception ex)
        {
            ImportStatusText.Text = $"读取 CSV 失败: {ex.Message}";
        }
    }

    private async void ImportStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string strategy) return;

        string csvText = ImportTextBox.Text;
        if (string.IsNullOrWhiteSpace(csvText))
        {
            ImportStatusText.Text = "请先粘贴 CSV 内容或选择文件，再选择导入策略。";
            return;
        }

        ImportStatusText.Text = "正在进行解析导入，请稍候...";

        var duplicateStrategy = strategy switch
        {
            "skip" => DuplicateStrategy.Skip,
            "merge" => DuplicateStrategy.MergeAlias,
            "overwrite" => DuplicateStrategy.Overwrite,
            _ => DuplicateStrategy.Skip,
        };

        try
        {
            await using var repository = new CatalogRepository(_repositoryOptions);
            await repository.InitializeAsync();
            var importSummary = await repository.ImportCsvWithSummaryAsync(csvText, duplicateStrategy);
            await CatalogRuntimeState.Current.RefreshSnapshotSummaryAsync();

            ImportStatusText.Text = importSummary.ErrorCount == 0
                ? $"导入完成：有效 {importSummary.ValidRowCount} 行，新增 {importSummary.InsertedCount}，覆盖 {importSummary.OverwrittenCount}，合并 {importSummary.MergedCount}，跳过 {importSummary.SkippedCount}。策略：{button.Content}。快照已刷新。"
                : $"导入完成：有效 {importSummary.ValidRowCount} 行，新增 {importSummary.InsertedCount}，覆盖 {importSummary.OverwrittenCount}，合并 {importSummary.MergedCount}，跳过 {importSummary.SkippedCount}；错误 {importSummary.ErrorCount} 行。";
            ImportErrorDetailsText.Text = FormatImportErrors(importSummary);
            ImportErrorDetailsText.Visibility = string.IsNullOrWhiteSpace(ImportErrorDetailsText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            CatalogRuntimeState.Current.NotifyCatalogChanged();
        }
        catch (Exception ex)
        {
            ImportStatusText.Text = $"导入过程中发生异常错误: {ex.Message}";
            ImportErrorDetailsText.Text = string.Empty;
            ImportErrorDetailsText.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatImportErrors(CatalogImportSummary importSummary)
    {
        if (importSummary.ErrorCount == 0)
        {
            return string.Empty;
        }

        var lines = importSummary.ParseResult.Errors
            .Take(20)
            .Select(error => $"第 {error.Line} 行：{error.Reason}")
            .ToList();

        if (importSummary.ErrorCount > lines.Count)
        {
            lines.Add($"还有 {importSummary.ErrorCount - lines.Count} 条错误未显示。");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
