using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Importing;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Transport;
using ComponentCatalogCore.Utilities;
using ComponentCatalogWinUI.Models;
using ComponentCatalogWinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ComponentCatalogWinUI.Pages;

public class SuggestionItem
{
    public string Value { get; set; } = string.Empty;
    public override string ToString() => Value;
}

public sealed partial class HomePage : Page, INotifyPropertyChanged
{
    private const string AllPackagesLabel = "全部封装";

    private string _selectedCategory = "全部";
    private ComponentItem? _selectedItem;
    private string _editableName = string.Empty;
    private string _editablePartNumber = string.Empty;
    private string _editableCategory = "未分类";
    private string _editablePackage = string.Empty;
    private string _editableLocation = string.Empty;
    private string _editableSourceTypeLabel = "手动录入";
    private string _editableAliases = string.Empty;
    private string _editableKeywords = string.Empty;
    private string _editableSubstitutes = string.Empty;
    private string _editableAttributes = string.Empty;
    private string _editableNotes = string.Empty;
    private bool _editableOwned;
    private string _lastActionText = "正在准备本地目录数据库…";
    private string _importStatusText = "导入入口已准备，数据会直接落到 SQLite。";
    private string _selectedPackageFilter = AllPackagesLabel;
    private string _selectedOwnershipFilter = "全部";
    private string _attributeFilterText = string.Empty;
    private readonly List<string> _availableCategories = [];
    private readonly CatalogRepositoryOptions _repositoryOptions;
    private readonly CatalogRepository _repository;
    private CatalogApiServer? _apiServer;
    private bool _isInitialized;

    private readonly ObservableCollection<ComponentItem> _allItems = [];

    public HomePage()
    {
        InitializeComponent();

        _repositoryOptions = CatalogStoragePaths.CreateRepositoryOptions();
        _repository = new CatalogRepository(_repositoryOptions);
        _availableCategories.Add("全部");
        CatalogRuntimeState.Current.UpdatePaths(_repositoryOptions.DatabasePath, _repositoryOptions.ExportPath);
        CatalogRuntimeState.Current.CatalogChanged += RuntimeCatalogChanged;

        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    public ObservableCollection<ComponentItem> FilteredItems { get; } = [];

    public ObservableCollection<string> AvailablePackages { get; } = [AllPackagesLabel];

    public ObservableCollection<string> OwnershipOptions { get; } = ["全部", "仅持有", "仅未持有"];

    public ObservableCollection<SuggestionItem> AvailableCategorySuggestions { get; } = [];

    public ObservableCollection<SuggestionItem> AvailablePackageSuggestions { get; } = [];

    public ObservableCollection<string> SourceTypeOptions { get; } = ["手动录入", "CSV 导入"];

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SidebarStateChanged;

    public ComponentItem? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (_selectedItem == value)
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedItem));
            OnPropertyChanged(nameof(DetailTitle));
            OnPropertyChanged(nameof(DetailSubtitle));
        }
    }

    public bool HasSelectedItem => SelectedItem is not null;

    public int TotalCount => _allItems.Count;

    public int OwnedCount => _allItems.Count(item => item.Owned);

    public int CategoryCount => _allItems
        .Select(item => item.Category)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public string ResultSummary => $"{FilteredItems.Count} 个结果 · {(_selectedCategory == "全部" ? "全部分类" : _selectedCategory)}";

    public string SearchTips => string.IsNullOrWhiteSpace(SearchBox?.Text)
        ? "支持中文、英文、数字、封装缩写和参数混合搜索，例如：10k 0603 1% 或 3.3V LDO SOT23"
        : $"当前搜索：{SearchBox.Text}";

    public string StorageStatusText => $"SQLite 真源：{_repositoryOptions.DatabasePath}";

    public string SnapshotStatusText => $"JSON 快照：{_repositoryOptions.ExportPath}";

    public string ApiBaseUrlText => _apiServer is null
        ? "本地读写 API：正在启动…"
        : $"本地读写 API：{_apiServer.BaseUrl}";

    public string SelectedPackageFilter
    {
        get => _selectedPackageFilter;
        set => SetField(ref _selectedPackageFilter, value);
    }

    public string SelectedOwnershipFilter
    {
        get => _selectedOwnershipFilter;
        set => SetField(ref _selectedOwnershipFilter, value);
    }

    public string AttributeFilterText
    {
        get => _attributeFilterText;
        set => SetField(ref _attributeFilterText, value);
    }

    public IReadOnlyList<string> AvailableCategoryFilters => _availableCategories;

    public string SelectedCategoryFilter => _selectedCategory;

    public Visibility EmptyStateVisibility => FilteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultListVisibility => FilteredItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string DetailTitle => SelectedItem?.Name ?? "新元件";

    public string DetailSubtitle => SelectedItem is null
        ? "创建新的目录型元件记录，首版先保留“是否持有”状态。"
        : $"{SelectedItem.Category} · {SelectedItem.PartNumber}";

    public string EditableName
    {
        get => _editableName;
        set => SetField(ref _editableName, value);
    }

    public string EditablePartNumber
    {
        get => _editablePartNumber;
        set => SetField(ref _editablePartNumber, value);
    }

    public string EditableCategory
    {
        get => _editableCategory;
        set => SetField(ref _editableCategory, value);
    }

    public string EditablePackage
    {
        get => _editablePackage;
        set => SetField(ref _editablePackage, value);
    }

    public string EditableLocation
    {
        get => _editableLocation;
        set => SetField(ref _editableLocation, value);
    }

    public string EditableSourceTypeLabel
    {
        get => _editableSourceTypeLabel;
        set => SetField(ref _editableSourceTypeLabel, value);
    }

    public string EditableAliases
    {
        get => _editableAliases;
        set => SetField(ref _editableAliases, value);
    }

    public string EditableKeywords
    {
        get => _editableKeywords;
        set => SetField(ref _editableKeywords, value);
    }

    public string EditableSubstitutes
    {
        get => _editableSubstitutes;
        set => SetField(ref _editableSubstitutes, value);
    }

    public string EditableAttributes
    {
        get => _editableAttributes;
        set => SetField(ref _editableAttributes, value);
    }

    public string EditableNotes
    {
        get => _editableNotes;
        set => SetField(ref _editableNotes, value);
    }

    public bool EditableOwned
    {
        get => _editableOwned;
        set => SetField(ref _editableOwned, value);
    }

    public string LastActionText
    {
        get => _lastActionText;
        set => SetField(ref _lastActionText, value);
    }

    public string ImportStatusText
    {
        get => _importStatusText;
        set => SetField(ref _importStatusText, value);
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        try
        {
            AppLogger.WritePage("HomePage.Loaded");
            await EnsureRepositoryReadyAsync();
            AppLogger.WritePage("HomePage.Ready");
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"HomePage.Error {ex}");
            var dialog = new ContentDialog
            {
                Title = "初始化失败",
                Content = $"页面加载时出错：{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    private async void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        CatalogRuntimeState.Current.CatalogChanged -= RuntimeCatalogChanged;

        if (_apiServer is not null)
        {
            await _apiServer.DisposeAsync();
            _apiServer = null;
        }

        await _repository.DisposeAsync();
    }

    private async void RuntimeCatalogChanged(object? sender, EventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            await ReloadAllItemsAsync(SelectedItem?.Id);
            LastActionText = "检测到目录数据变更，已自动刷新列表。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"RuntimeCatalogChanged.Error {ex}");
            LastActionText = $"自动刷新失败：{ex.Message}";
        }
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_isInitialized)
        {
            return;
        }

        await RefreshFilteredItemsAsync();
        OnPropertyChanged(nameof(SearchTips));
    }

    private void CategoryEditorBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            RefreshCategorySuggestions(sender.Text);
        }
    }

    private void CategoryEditorBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        CategoryEditorBox.ItemsSource = AvailableCategorySuggestions;
        CategoryEditorBox.IsSuggestionListOpen = true;
        CategoryEditorBox.Focus(FocusState.Programmatic);
    }

    private void PackageEditorBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            RefreshPackageSuggestions(sender.Text);
        }
    }

    private void PackageEditorBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        PackageEditorBox.ItemsSource = AvailablePackageSuggestions;
        PackageEditorBox.IsSuggestionListOpen = true;
        PackageEditorBox.Focus(FocusState.Programmatic);
    }

    private void ComponentGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ComponentItem item)
        {
            SelectItem(item);
        }
    }

    private void NewComponentButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedItem = null;
        EditableName = string.Empty;
        EditablePartNumber = string.Empty;
        EditableCategory = _selectedCategory == "全部" ? "未分类" : _selectedCategory;
        EditablePackage = string.Empty;
        EditableLocation = string.Empty;
        EditableSourceTypeLabel = "手动录入";
        EditableAliases = string.Empty;
        EditableKeywords = string.Empty;
        EditableSubstitutes = string.Empty;
        EditableAttributes = string.Empty;
        EditableNotes = string.Empty;
        EditableOwned = true;
        LastActionText = "已切换到新建模式。";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var normalizedName = string.IsNullOrWhiteSpace(EditableName) ? "未命名元件" : EditableName.Trim();
            var normalizedPartNumber = string.IsNullOrWhiteSpace(EditablePartNumber) ? normalizedName : EditablePartNumber.Trim();
            var normalizedCategory = string.IsNullOrWhiteSpace(EditableCategory) ? "未分类" : EditableCategory.Trim();

            var existingItem = SelectedItem;
            var component = new ComponentRecord
            {
                Id = existingItem?.Id ?? GenerateId(normalizedPartNumber),
                Name = normalizedName,
                PartNumber = normalizedPartNumber,
                Category = normalizedCategory,
                Package = EditablePackage.Trim(),
                Aliases = CatalogTextCodec.ParseList(EditableAliases),
                Keywords = CatalogTextCodec.ParseList(EditableKeywords),
                Attributes = CatalogImportService.ParseAttributes(EditableAttributes).ToDictionary(pair => pair.Key, pair => pair.Value),
                Owned = EditableOwned,
                Location = EditableLocation.Trim(),
                Substitutes = CatalogTextCodec.ParseList(EditableSubstitutes),
                Notes = EditableNotes.Trim(),
                SourceType = ResolveEditableSourceType(),
            };

            await _repository.UpsertComponentAsync(component);
            await ReloadAllItemsAsync(component.Id);
            CatalogRuntimeState.Current.NotifyCatalogChanged();
            LastActionText = existingItem is null
                ? $"已新增 {normalizedName}，并写入 SQLite。"
                : $"已保存 {normalizedName}，并刷新 JSON 快照。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"SaveButton.Error {ex}");
            LastActionText = $"保存失败：{ex.Message}";
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
        {
            return;
        }

        try
        {
            var deletedName = SelectedItem.Name;
            await _repository.DeleteByIdAsync(SelectedItem.Id);
            await ReloadAllItemsAsync();
            CatalogRuntimeState.Current.NotifyCatalogChanged();
            LastActionText = $"已删除 {deletedName}，并更新本地快照。";
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"DeleteButton.Error {ex}");
            LastActionText = $"删除失败：{ex.Message}";
        }
    }

    public async Task ExecuteBulkImportAsync(string csvText, string strategy, string strategyDisplayName)
    {
        if (string.IsNullOrWhiteSpace(csvText))
        {
            ImportStatusText = "请先粘贴 CSV 内容，再选择导入策略。";
            NotifySummaryStateChanged();
            return;
        }

        var duplicateStrategy = strategy switch
        {
            "skip" => DuplicateStrategy.Skip,
            "merge" => DuplicateStrategy.MergeAlias,
            "overwrite" => DuplicateStrategy.Overwrite,
            _ => DuplicateStrategy.Skip,
        };

        var importSummary = await _repository.ImportCsvWithSummaryAsync(csvText, duplicateStrategy);
        await ReloadAllItemsAsync();
        CatalogRuntimeState.Current.NotifyCatalogChanged();

        ImportStatusText = importSummary.ErrorCount == 0
            ? $"已处理 {importSummary.ValidRowCount} 行有效 CSV，新增 {importSummary.InsertedCount}，覆盖 {importSummary.OverwrittenCount}，合并 {importSummary.MergedCount}，跳过 {importSummary.SkippedCount}；策略：{strategyDisplayName}。"
            : $"已处理 {importSummary.ValidRowCount} 行，错误 {importSummary.ErrorCount} 行；首个错误在第 {importSummary.ParseResult.Errors[0].Line} 行。";
        NotifySummaryStateChanged();
    }

    public async Task ApplyCategoryFilterAsync(string category)
    {
        _selectedCategory = string.IsNullOrWhiteSpace(category) ? "全部" : category;
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplyPackageFilterAsync(string? package)
    {
        SelectedPackageFilter = string.IsNullOrWhiteSpace(package) ? AllPackagesLabel : package;
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplyOwnershipFilterAsync(string? ownership)
    {
        SelectedOwnershipFilter = string.IsNullOrWhiteSpace(ownership) ? "全部" : ownership;
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplyAttributeFilterAsync(string? attributeText)
    {
        AttributeFilterText = attributeText ?? string.Empty;
        await RefreshFilteredItemsAsync();
    }

    private async Task EnsureRepositoryReadyAsync()
    {
        AppLogger.WritePage("Repository.Init start");
        await _repository.InitializeAsync();

        if (!(await _repository.ListComponentsAsync()).Any())
        {
            foreach (var component in CatalogDesignSeed.CreateSampleComponents())
            {
                await _repository.UpsertComponentAsync(component);
            }
        }

        if (_apiServer is null)
        {
            _apiServer = await CatalogApiServer.StartAsync(_repository);
            _apiServer.CatalogChanged += (sender, e) =>
            {
                CatalogRuntimeState.Current.NotifyCatalogChanged();
            };
        }
        CatalogRuntimeState.Current.UpdateApiBaseUrl(_apiServer.BaseUrl);

        await ReloadAllItemsAsync();
        LastActionText = "SQLite 目录已连接，搜索与编辑现在会写入本地真源。";
        ImportStatusText = "CSV 导入已接入真实仓库，可按策略处理重复型号。";
        await CatalogRuntimeState.Current.RefreshSnapshotSummaryAsync();
        AppLogger.WritePage("Repository.Init done");
        AppLogger.WritePage($"Api.Started {_apiServer.BaseUrl}");
    }

    private async Task ReloadAllItemsAsync(string? preferredSelectionId = null)
    {
        _allItems.Clear();
        foreach (var item in (await _repository.ListComponentsAsync()).Select(ComponentItem.FromRecord))
        {
            _allItems.Add(item);
        }

        await RefreshAvailableCategoriesAsync();
        RefreshAvailablePackages();
        RefreshEditSuggestions();
        await RefreshFilteredItemsAsync(preferredSelectionId);
        await CatalogRuntimeState.Current.RefreshSnapshotSummaryAsync();
    }

    private async Task RefreshAvailableCategoriesAsync()
    {
        _availableCategories.Clear();
        _availableCategories.AddRange(await _repository.ListCategoriesAsync());
        if (!_availableCategories.Contains(_selectedCategory, StringComparer.OrdinalIgnoreCase))
        {
            _selectedCategory = "全部";
        }

        NotifySidebarStateChanged();
    }

    private void RefreshAvailablePackages()
    {
        var currentSelection = SelectedPackageFilter;
        var packages = _allItems
            .Select(item => item.Package)
            .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(packageName => packageName, StringComparer.CurrentCulture)
            .ToList();

        AvailablePackages.Clear();
        AvailablePackages.Add(AllPackagesLabel);
        foreach (var packageName in packages)
        {
            AvailablePackages.Add(packageName);
        }

        SelectedPackageFilter = AvailablePackages.Contains(currentSelection)
            ? currentSelection
            : AllPackagesLabel;
    }

    private void RefreshEditSuggestions()
    {
        var categories = _allItems
            .Select(item => item.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCulture)
            .ToList();

        AvailableCategorySuggestions.Clear();
        foreach (var category in categories)
        {
            AvailableCategorySuggestions.Add(new SuggestionItem { Value = category });
        }

        var packages = _allItems
            .Select(item => item.Package)
            .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(packageName => packageName, StringComparer.CurrentCulture)
            .ToList();

        AvailablePackageSuggestions.Clear();
        foreach (var packageName in packages)
        {
            AvailablePackageSuggestions.Add(new SuggestionItem { Value = packageName });
        }
    }

    private void RefreshCategorySuggestions(string input)
    {
        if (CategoryEditorBox is null)
        {
            return;
        }

        CategoryEditorBox.ItemsSource = FilterSuggestionValues(AvailableCategorySuggestions, input);
    }

    private void RefreshPackageSuggestions(string input)
    {
        if (PackageEditorBox is null)
        {
            return;
        }

        PackageEditorBox.ItemsSource = FilterSuggestionValues(AvailablePackageSuggestions, input);
    }

    private static IReadOnlyList<SuggestionItem> FilterSuggestionValues(IEnumerable<SuggestionItem> values, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return values.ToList();
        }

        return values
            .Where(item => item.Value.Contains(input, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task RefreshFilteredItemsAsync(string? preferredSelectionId = null)
    {
        FilteredItems.Clear();
        var currentSelectionId = preferredSelectionId ?? SelectedItem?.Id;
        var results = await _repository.SearchAsync(new SearchQuery
        {
            Text = SearchBox?.Text ?? string.Empty,
            Category = ResolveSelectedCategory(),
            Package = ResolveSelectedPackage(),
            Owned = ResolveOwnedFilter(),
            AttributeText = string.IsNullOrWhiteSpace(AttributeFilterText) ? null : AttributeFilterText.Trim(),
        });

        foreach (var result in results.Select(item => ComponentItem.FromRecord(item.Component)))
        {
            FilteredItems.Add(result);
        }

        var nextSelection = FilteredItems.FirstOrDefault(item => item.Id == currentSelectionId) ?? FilteredItems.FirstOrDefault();
        SelectItem(nextSelection);
        NotifySummaryStateChanged();
        NotifySidebarStateChanged();
    }

    private string ResolveSelectedCategory() => _selectedCategory;

    private string? ResolveSelectedPackage()
    {
        return string.Equals(SelectedPackageFilter, AllPackagesLabel, StringComparison.Ordinal)
            ? null
            : SelectedPackageFilter;
    }

    private bool? ResolveOwnedFilter()
    {
        return SelectedOwnershipFilter switch
        {
            "仅持有" => true,
            "仅未持有" => false,
            _ => null,
        };
    }

    private void SelectItem(ComponentItem? item)
    {
        SelectedItem = item;
        EditableName = item?.Name ?? string.Empty;
        EditablePartNumber = item?.PartNumber ?? string.Empty;
        EditableCategory = item?.Category ?? "未分类";
        EditablePackage = item?.Package ?? string.Empty;
        EditableLocation = item?.Location ?? string.Empty;
        EditableSourceTypeLabel = item is null ? "手动录入" : FormatSourceTypeLabel(item.SourceType);
        EditableAliases = item is null ? string.Empty : ComponentItem.FormatList(item.Aliases);
        EditableKeywords = item is null ? string.Empty : ComponentItem.FormatList(item.Keywords);
        EditableSubstitutes = item is null ? string.Empty : ComponentItem.FormatList(item.Substitutes);
        EditableAttributes = item?.Attributes ?? string.Empty;
        EditableNotes = item?.Notes ?? string.Empty;
        EditableOwned = item?.Owned ?? true;
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));
    }

    private void NotifySummaryStateChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(OwnedCount));
        OnPropertyChanged(nameof(CategoryCount));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ResultListVisibility));
        OnPropertyChanged(nameof(ApiBaseUrlText));
        OnPropertyChanged(nameof(StorageStatusText));
        OnPropertyChanged(nameof(SnapshotStatusText));
    }

    private string GenerateId(string seed)
    {
        var normalized = seed.Trim().ToLowerInvariant().Replace(" ", "-");
        var candidate = string.IsNullOrWhiteSpace(normalized) ? $"component-{Guid.NewGuid():N}" : normalized;

        return _allItems.Any(item => item.Id == candidate)
            ? $"{candidate}-{Guid.NewGuid().ToString("N")[..6]}"
            : candidate;
    }

    private ComponentSourceType ResolveEditableSourceType()
    {
        return EditableSourceTypeLabel switch
        {
            "CSV 导入" => ComponentSourceType.CsvImport,
            _ => ComponentSourceType.Manual,
        };
    }

    private static string FormatSourceTypeLabel(ComponentSourceType sourceType)
    {
        return sourceType switch
        {
            ComponentSourceType.CsvImport => "CSV 导入",
            _ => "手动录入",
        };
    }

    private void ComponentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double availableWidth = e.NewSize.Width - 16;
        if (availableWidth <= 0) return;

        double minWidth = 280; // 最小卡片宽度阈值
        int columns = (int)Math.Max(1, Math.Floor(availableWidth / minWidth));
        double calculatedWidth = (availableWidth / columns) - 12; // 扣除 Margin 间隙

        if (ComponentGrid.ItemsPanelRoot is ItemsWrapGrid itemsWrapGrid)
        {
            itemsWrapGrid.ItemWidth = calculatedWidth;
            itemsWrapGrid.ItemHeight = 145; // 固定高度
        }
    }

    private void NotifySidebarStateChanged()
    {
        SidebarStateChanged?.Invoke(this, EventArgs.Empty);
        RefreshLocalSidebar();
    }

    private bool _isSyncingLocalSidebar;
    private void RefreshLocalSidebar()
    {
        _isSyncingLocalSidebar = true;
        try
        {
            if (SidebarCategoryHost is null) return;

            SidebarCategoryHost.Children.Clear();
            foreach (var category in AvailableCategoryFilters)
            {
                var button = new Button
                {
                    Content = category,
                    Tag = category,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Style = string.Equals(category, SelectedCategoryFilter, StringComparison.Ordinal)
                        ? (Style)Application.Current.Resources["AccentButtonStyle"]
                        : null,
                };
                button.Click += SidebarCategoryButton_Click;
                SidebarCategoryHost.Children.Add(button);
            }

            if (SidebarPackageComboBox is not null)
            {
                SidebarPackageComboBox.ItemsSource = AvailablePackages;
                SidebarPackageComboBox.SelectedItem = SelectedPackageFilter;
            }

            if (SidebarOwnedComboBox is not null)
            {
                SidebarOwnedComboBox.ItemsSource = OwnershipOptions;
                SidebarOwnedComboBox.SelectedItem = SelectedOwnershipFilter;
            }

            if (SidebarAttributeTextBox is not null)
            {
                SidebarAttributeTextBox.Text = AttributeFilterText;
            }
        }
        finally
        {
            _isSyncingLocalSidebar = false;
        }
    }

    private async void SidebarCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string category) return;
        await ApplyCategoryFilterAsync(category);
    }

    private async void SidebarPackageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingLocalSidebar) return;
        await ApplyPackageFilterAsync(SidebarPackageComboBox.SelectedItem as string);
    }

    private async void SidebarOwnedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingLocalSidebar) return;
        await ApplyOwnershipFilterAsync(SidebarOwnedComboBox.SelectedItem as string);
    }

    private async void SidebarAttributeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingLocalSidebar) return;
        await ApplyAttributeFilterAsync(SidebarAttributeTextBox.Text);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }
}
