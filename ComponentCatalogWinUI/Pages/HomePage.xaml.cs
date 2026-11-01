using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Importing;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Search;
using ComponentCatalogCore.Taxonomy;
using ComponentCatalogCore.Utilities;
using ComponentCatalogWinUI.Models;
using ComponentCatalogWinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ComponentCatalogWinUI.Pages;

public class SuggestionItem
{
    public string Value { get; set; } = string.Empty;
    public override string ToString() => Value;
}

public sealed class CategoryChoice
{
    public CategoryChoice(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; set; }

    public string Label { get; set; }
}

public sealed partial class HomePage : Page, INotifyPropertyChanged
{
    private const string AllPackagesLabel = "全部封装";
    private const int FilterInputDebounceDelayMilliseconds = 250;
    private const int ComponentGridResizeDebounceDelayMilliseconds = 80;
    // These breakpoints use the measured HomePage width after NavigationView has
    // already reserved its pane. The state-aware thresholds prevent pane reparenting
    // from thrashing while the user drags near a breakpoint.
    private const double MediumLayoutMinimumWidth = 720;
    private const double WideLayoutEnterWidth = 1120;
    private const double WideLayoutExitWidth = 1080;
    private const double ExtraWideLayoutEnterWidth = 1640;
    private const double ExtraWideLayoutExitWidth = 1560;
    private const double ExtraWideFilterPaneWidth = 232;
    private const double DockedEditorPaneWidth = 360;

    private readonly Flyout _filterFlyout = new();
    private ContentDialog? _editorOverlay;
    private bool _isEditorOverlayOpen;
    private bool _isEditorRequested;
    private bool _restoreFilterPaneAfterFlyoutCloses;
    private ResponsiveLayoutMode _responsiveLayout = ResponsiveLayoutMode.Unknown;
    private string _selectedMajorCategoryId = string.Empty;
    private string _selectedSubCategoryId = string.Empty;
    private ComponentItem? _selectedItem;
    private string _editableName = string.Empty;
    private string _editablePartNumber = string.Empty;
    private string _editableMajorCategoryId = ComponentCategoryTaxonomy.OtherMajorCategoryId;
    private string _editableSubCategoryId = ComponentCategoryTaxonomy.UncategorizedSubCategoryId;
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
    private readonly CatalogWorkspaceService _workspaceService;
    private readonly CatalogRepositoryOptions _repositoryOptions;
    private readonly CatalogRepository _repository;
    private readonly DispatcherQueueTimer _componentGridResizeTimer;
    private XamlRoot? _observedXamlRoot;
    private bool _isInitialized;
    private bool _isSynchronizingGridSelection;
    private bool _skipNextRuntimeCatalogRefresh;
    private int _filterRefreshRequestId;
    private double _pendingComponentGridWidth;

    private readonly ObservableCollection<ComponentItem> _allItems = [];
    private readonly List<ComponentRecord> _allRecords = [];
    private readonly Dictionary<string, ComponentItem> _itemsById = new(StringComparer.Ordinal);

    public HomePage()
    {
        InitializeComponent();

        _componentGridResizeTimer = DispatcherQueue.CreateTimer();
        _componentGridResizeTimer.Interval = TimeSpan.FromMilliseconds(ComponentGridResizeDebounceDelayMilliseconds);
        _componentGridResizeTimer.IsRepeating = false;
        _componentGridResizeTimer.Tick += ComponentGridResizeTimer_Tick;

        _workspaceService = CatalogWorkspaceService.Current;
        _repositoryOptions = _workspaceService.Options;
        _repository = _workspaceService.Repository;
        LoadTaxonomyOptions();
        _filterFlyout.Closed += FilterFlyout_Closed;
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

    public string CompactStatisticsSummary => $"目录 {TotalCount} · 持有 {OwnedCount} · 分类 {CategoryCount}";

    public string ResultSummary => $"{FilteredItems.Count} 个结果 · {ResolveSelectedCategorySummary()}";

    public string SearchTips => string.IsNullOrWhiteSpace(SearchBox?.Text)
        ? "支持中文、英文、数字、封装缩写和参数混合搜索，例如：10k 0603 1% 或 3.3V LDO SOT23"
        : $"当前搜索：{SearchBox.Text}";

    public string StorageStatusText => $"SQLite 真源：{_repositoryOptions.DatabasePath}";

    public string SnapshotStatusText => $"JSON 快照：{_repositoryOptions.ExportPath}";

    public string ApiBaseUrlText => $"本地读写 API：{CatalogRuntimeState.Current.ApiStatusText}";

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

    public ObservableCollection<CategoryChoice> MajorCategoryFilters { get; } = [];

    public ObservableCollection<CategoryChoice> SubcategoryFilters { get; } = [];

    public ObservableCollection<CategoryChoice> EditorMajorCategories { get; } = [];

    public ObservableCollection<CategoryChoice> EditorSubcategories { get; } = [];

    public string SelectedMajorCategoryId
    {
        get => _selectedMajorCategoryId;
        set => SetField(ref _selectedMajorCategoryId, value ?? string.Empty);
    }

    public string SelectedSubCategoryId
    {
        get => _selectedSubCategoryId;
        set => SetField(ref _selectedSubCategoryId, value ?? string.Empty);
    }

    public bool IsSubcategoryFilterEnabled => !string.IsNullOrEmpty(SelectedMajorCategoryId);

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

    public string EditableMajorCategoryId
    {
        get => _editableMajorCategoryId;
        set
        {
            if (SetField(ref _editableMajorCategoryId, value ?? ComponentCategoryTaxonomy.OtherMajorCategoryId))
            {
                RefreshEditorSubcategories();
            }
        }
    }

    public string EditableSubCategoryId
    {
        get => _editableSubCategoryId;
        set => SetField(ref _editableSubCategoryId, value ?? ComponentCategoryTaxonomy.UncategorizedSubCategoryId);
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
        UiMotion.ConfigurePanelMotion(EditorPane, fromRight: true);
        UiMotion.ConfigurePanelMotion(FilterPane, fromRight: false);

        ObserveXamlRoot();
        ApplyResponsiveLayout(GetResponsiveContentWidth(HomeLayoutRoot.ActualWidth));

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

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelPendingFilterRefresh();
        _componentGridResizeTimer.Stop();
        CatalogRuntimeState.Current.CatalogChanged -= RuntimeCatalogChanged;
        if (_observedXamlRoot is not null)
        {
            _observedXamlRoot.Changed -= XamlRoot_Changed;
            _observedXamlRoot = null;
        }
    }

    private async void RuntimeCatalogChanged(object? sender, EventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (_skipNextRuntimeCatalogRefresh)
        {
            _skipNextRuntimeCatalogRefresh = false;
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

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_isInitialized)
        {
            return;
        }

        OnPropertyChanged(nameof(SearchTips));
        QueueDebouncedFilterRefresh();
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

    private async void ComponentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingGridSelection)
        {
            return;
    }

        if (sender is GridView gridView && gridView.SelectedItem is ComponentItem item)
    {
            SelectItem(item, synchronizeGridSelection: false);
            RequestEditor();
            await ShowEditorOverlayIfNarrowAsync();
        }
    }

    private async void ComponentCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ComponentItem item)
        {
            SelectItem(item);
            RequestEditor();
            await ShowEditorOverlayIfNarrowAsync();
        }
    }

    private async void NewComponentButton_Click(object sender, RoutedEventArgs e)
    {
        SelectItem(null);
        EditableMajorCategoryId = string.IsNullOrEmpty(SelectedMajorCategoryId)
            ? ComponentCategoryTaxonomy.OtherMajorCategoryId
            : SelectedMajorCategoryId;
        EditableSubCategoryId = string.IsNullOrEmpty(SelectedSubCategoryId)
            ? ComponentCategoryTaxonomy.UncategorizedSubCategoryId
            : SelectedSubCategoryId;
        LastActionText = "已切换到新建模式。";
        RequestEditor();
        await ShowEditorOverlayIfNarrowAsync();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_responsiveLayout == ResponsiveLayoutMode.ExtraWide)
        {
            return;
        }

        MoveFilterPaneToFlyout();
        _filterFlyout.ShowAt(FilterButton);
    }

    private void EditorCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _isEditorRequested = false;
        UpdateEditorPanePlacement();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var normalizedName = string.IsNullOrWhiteSpace(EditableName) ? "未命名元件" : EditableName.Trim();
            var normalizedPartNumber = string.IsNullOrWhiteSpace(EditablePartNumber) ? normalizedName : EditablePartNumber.Trim();
            var assignment = ComponentCategoryTaxonomy.GetAssignment(EditableMajorCategoryId, EditableSubCategoryId);

            var existingItem = SelectedItem;
            var component = new ComponentRecord
            {
                Id = existingItem?.Id ?? GenerateId(normalizedPartNumber),
                Name = normalizedName,
                PartNumber = normalizedPartNumber,
                Category = assignment.SubCategory,
                MajorCategoryId = assignment.MajorCategoryId,
                SubCategoryId = assignment.SubCategoryId,
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

            await _repository.SaveComponentAsync(component);
            await ReloadAllItemsAsync(component.Id);
            _skipNextRuntimeCatalogRefresh = true;
            CatalogRuntimeState.Current.NotifyCatalogChanged();
            LastActionText = existingItem is null
                ? $"已新增 {normalizedName}，并写入 SQLite。"
                : $"已保存 {normalizedName}，并刷新 JSON 快照。";
        }
        catch (DuplicatePartNumberException)
        {
            LastActionText = "保存失败：型号已被另一条元件记录使用。";
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

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除该元件？",
            Content = $"将删除“{SelectedItem.Name}”（{SelectedItem.PartNumber}），并同步更新 JSON 快照。删除后无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var deletedName = SelectedItem.Name;
            await _repository.DeleteByIdAsync(SelectedItem.Id);
            await ReloadAllItemsAsync();
            _skipNextRuntimeCatalogRefresh = true;
            CatalogRuntimeState.Current.NotifyCatalogChanged();
            _isEditorRequested = false;
            UpdateEditorPanePlacement();
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
    }

    public async Task ApplyMajorCategoryFilterAsync(string? majorCategoryId)
    {
        var nextMajorCategoryId = majorCategoryId ?? string.Empty;
        CancelPendingFilterRefresh();
        SelectedMajorCategoryId = nextMajorCategoryId;
        SelectedSubCategoryId = string.Empty;
        RefreshSubcategoryFilters();
        NotifySidebarStateChanged();
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplySubcategoryFilterAsync(string? subCategoryId)
    {
        var nextSubCategoryId = subCategoryId ?? string.Empty;
        CancelPendingFilterRefresh();
        SelectedSubCategoryId = nextSubCategoryId;
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplyPackageFilterAsync(string? package)
    {
        var nextPackage = string.IsNullOrWhiteSpace(package) ? AllPackagesLabel : package;
        if (string.Equals(SelectedPackageFilter, nextPackage, StringComparison.Ordinal))
        {
            return;
        }

        CancelPendingFilterRefresh();
        SelectedPackageFilter = nextPackage;
        SyncSidebarFilterControls();
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplyOwnershipFilterAsync(string? ownership)
    {
        var nextOwnership = string.IsNullOrWhiteSpace(ownership) ? "全部" : ownership;
        if (string.Equals(SelectedOwnershipFilter, nextOwnership, StringComparison.Ordinal))
        {
            return;
        }

        CancelPendingFilterRefresh();
        SelectedOwnershipFilter = nextOwnership;
        SyncSidebarFilterControls();
        await RefreshFilteredItemsAsync();
    }

    public async Task ApplyAttributeFilterAsync(string? attributeText)
    {
        var nextAttributeText = attributeText ?? string.Empty;
        if (string.Equals(AttributeFilterText, nextAttributeText, StringComparison.Ordinal))
        {
            return;
        }

        CancelPendingFilterRefresh();
        AttributeFilterText = nextAttributeText;
        SyncSidebarFilterControls();
        await RefreshFilteredItemsAsync();
    }

    private async Task EnsureRepositoryReadyAsync()
    {
        AppLogger.WritePage("Repository.Init start");
        await _workspaceService.EnsureInitializedAsync();

        if (!(await _repository.ListComponentsAsync()).Any())
        {
            foreach (var component in CatalogDesignSeed.CreateSampleComponents())
            {
                await _repository.SaveComponentAsync(component);
        }
        }

        await ReloadAllItemsAsync();
        LastActionText = "SQLite 目录已连接，搜索与编辑现在会写入本地真源。";
        ImportStatusText = "CSV 导入已接入真实仓库，可按策略处理重复型号。";
        await CatalogRuntimeState.Current.RefreshSnapshotSummaryAsync();
        AppLogger.WritePage("Repository.Init done");
        if (_workspaceService.ApiServer is not null)
        {
            AppLogger.WritePage($"Api.Started {_workspaceService.ApiServer.BaseUrl}");
        }
    }

    private async Task ReloadAllItemsAsync(string? preferredSelectionId = null)
    {
        CancelPendingFilterRefresh();
        var records = await _repository.ListComponentsAsync();

        _allItems.Clear();
        _allRecords.Clear();
        _itemsById.Clear();
        foreach (var record in records)
        {
            var item = ComponentItem.FromRecord(record);
            _allItems.Add(item);
            _allRecords.Add(record);
            _itemsById[item.Id] = item;
        }

        RefreshAvailablePackages();
        RefreshEditSuggestions();
        NotifyCatalogStateChanged();
        await RefreshFilteredItemsAsync(preferredSelectionId);
        await CatalogRuntimeState.Current.RefreshSnapshotSummaryAsync();
    }

    private void LoadTaxonomyOptions()
    {
        MajorCategoryFilters.Clear();
        MajorCategoryFilters.Add(new CategoryChoice(string.Empty, "全部"));
        EditorMajorCategories.Clear();
        foreach (var major in ComponentCategoryTaxonomy.MajorCategories)
        {
            MajorCategoryFilters.Add(new CategoryChoice(major.Id, major.Label));
            EditorMajorCategories.Add(new CategoryChoice(major.Id, major.Label));
        }

        RefreshSubcategoryFilters();
        RefreshEditorSubcategories();
    }

    private void RefreshSubcategoryFilters()
    {
        SubcategoryFilters.Clear();
        SubcategoryFilters.Add(new CategoryChoice(string.Empty, "全部"));
        var major = ComponentCategoryTaxonomy.FindMajor(SelectedMajorCategoryId);
        if (major is not null)
        {
            foreach (var subcategory in major.Subcategories)
            {
                SubcategoryFilters.Add(new CategoryChoice(subcategory.Id, subcategory.Label));
            }
        }

        if (!SubcategoryFilters.Any(choice => string.Equals(choice.Id, SelectedSubCategoryId, StringComparison.Ordinal)))
        {
            SelectedSubCategoryId = string.Empty;
        }

        OnPropertyChanged(nameof(IsSubcategoryFilterEnabled));
    }

    private void RefreshEditorSubcategories()
    {
        EditorSubcategories.Clear();
        var major = ComponentCategoryTaxonomy.FindMajor(EditableMajorCategoryId)
            ?? ComponentCategoryTaxonomy.FindMajor(ComponentCategoryTaxonomy.OtherMajorCategoryId)!;
        foreach (var subcategory in major.Subcategories)
        {
            EditorSubcategories.Add(new CategoryChoice(subcategory.Id, subcategory.Label));
        }

        if (!EditorSubcategories.Any(choice => string.Equals(choice.Id, EditableSubCategoryId, StringComparison.Ordinal)))
        {
            EditableSubCategoryId = major.Subcategories.First().Id;
        }
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

    private void QueueDebouncedFilterRefresh()
    {
        var requestId = ++_filterRefreshRequestId;
        _ = RunDebouncedFilterRefreshAsync(requestId);
    }

    private void CancelPendingFilterRefresh()
    {
        _filterRefreshRequestId++;
    }

    private async Task RunDebouncedFilterRefreshAsync(int requestId)
    {
        try
        {
            await Task.Delay(FilterInputDebounceDelayMilliseconds);
            if (requestId != _filterRefreshRequestId || !_isInitialized)
            {
                return;
            }

            await RefreshFilteredItemsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.WritePage($"HomePage.FilterRefresh.Error {ex}");
        }
    }

    private Task RefreshFilteredItemsAsync(string? preferredSelectionId = null)
    {
        FilteredItems.Clear();
        var currentSelectionId = preferredSelectionId ?? SelectedItem?.Id;
        var results = CatalogSearchService.Search(_allRecords, new SearchQuery
        {
            Text = SearchBox?.Text ?? string.Empty,
            Category = "全部",
            MajorCategoryId = string.IsNullOrEmpty(SelectedMajorCategoryId) ? null : SelectedMajorCategoryId,
            SubCategoryId = string.IsNullOrEmpty(SelectedSubCategoryId) ? null : SelectedSubCategoryId,
            Package = ResolveSelectedPackage(),
            Owned = ResolveOwnedFilter(),
            AttributeText = string.IsNullOrWhiteSpace(AttributeFilterText) ? null : AttributeFilterText.Trim(),
        });

        foreach (var result in results)
        {
            if (_itemsById.TryGetValue(result.Component.Id, out var item))
        {
                FilteredItems.Add(item);
            }
        }

        var nextSelection = currentSelectionId is null
            ? null
            : FilteredItems.FirstOrDefault(item => item.Id == currentSelectionId);
        SelectItem(nextSelection);
        NotifyFilteredResultsStateChanged();
        return Task.CompletedTask;
    }

    private string ResolveSelectedCategorySummary()
    {
        var major = ComponentCategoryTaxonomy.FindMajor(SelectedMajorCategoryId);
        var subcategory = ComponentCategoryTaxonomy.FindSubcategory(SelectedSubCategoryId);
        return subcategory?.Label ?? major?.Label ?? "全部分类";
    }

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

    private void SelectItem(ComponentItem? item, bool synchronizeGridSelection = true)
    {
        UpdateSelectedCardState(item);
        if (synchronizeGridSelection)
        {
            SyncGridSelection(item);
        }

        ApplySelectedItem(item);
    }

    private void SyncGridSelection(ComponentItem? item)
    {
        if (ComponentGrid is null || ReferenceEquals(ComponentGrid.SelectedItem, item))
        {
            return;
        }

        _isSynchronizingGridSelection = true;
        try
    {
            ComponentGrid.SelectedItem = item;
        }
        finally
        {
            _isSynchronizingGridSelection = false;
        }
    }

    private void UpdateSelectedCardState(ComponentItem? item)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            if (item is not null && !item.IsSelected)
            {
                item.IsSelected = true;
            }

            return;
        }

        if (_selectedItem is not null)
        {
            _selectedItem.IsSelected = false;
        }

        if (item is not null)
        {
            item.IsSelected = true;
        }
    }

    private void ApplySelectedItem(ComponentItem? item)
    {
        if (ReferenceEquals(SelectedItem, item))
        {
            return;
        }

        SelectedItem = item;
        EditableName = item?.Name ?? string.Empty;
        EditablePartNumber = item?.PartNumber ?? string.Empty;
        EditableMajorCategoryId = item?.MajorCategoryId ?? ComponentCategoryTaxonomy.OtherMajorCategoryId;
        EditableSubCategoryId = item?.SubCategoryId ?? ComponentCategoryTaxonomy.UncategorizedSubCategoryId;
        EditablePackage = item?.Package ?? string.Empty;
        EditableLocation = item?.Location ?? string.Empty;
        EditableSourceTypeLabel = item is null ? "手动录入" : FormatSourceTypeLabel(item.SourceType);
        EditableAliases = item is null ? string.Empty : ComponentItem.FormatList(item.Aliases);
        EditableKeywords = item is null ? string.Empty : ComponentItem.FormatList(item.Keywords);
        EditableSubstitutes = item is null ? string.Empty : ComponentItem.FormatList(item.Substitutes);
        EditableAttributes = item?.Attributes ?? string.Empty;
        EditableNotes = item?.Notes ?? string.Empty;
        EditableOwned = item?.Owned ?? true;
    }

    private void NotifyCatalogStateChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(OwnedCount));
        OnPropertyChanged(nameof(CategoryCount));
        OnPropertyChanged(nameof(CompactStatisticsSummary));
        OnPropertyChanged(nameof(ApiBaseUrlText));
        OnPropertyChanged(nameof(StorageStatusText));
        OnPropertyChanged(nameof(SnapshotStatusText));
    }

    private void NotifyFilteredResultsStateChanged()
    {
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ResultListVisibility));
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

    private void HomeLayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(GetResponsiveContentWidth(e.NewSize.Width));
    }

    private void ObserveXamlRoot()
    {
        if (ReferenceEquals(_observedXamlRoot, XamlRoot))
        {
            return;
        }

        if (_observedXamlRoot is not null)
        {
            _observedXamlRoot.Changed -= XamlRoot_Changed;
        }

        _observedXamlRoot = XamlRoot;
        if (_observedXamlRoot is not null)
        {
            _observedXamlRoot.Changed += XamlRoot_Changed;
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        ApplyResponsiveLayout(GetResponsiveContentWidth(HomeLayoutRoot.ActualWidth));
    }

    private double GetResponsiveContentWidth(double fallbackWidth)
    {
        if (HomeLayoutRoot?.ActualWidth > 0)
        {
            return HomeLayoutRoot.ActualWidth;
        }

        return fallbackWidth > 0
            ? fallbackWidth
            : XamlRoot?.Size.Width ?? 0;
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var nextLayout = ResolveResponsiveLayout(width, _responsiveLayout);
        if (_responsiveLayout == nextLayout)
        {
            return;
        }

        _responsiveLayout = nextLayout;
        var isCompact = nextLayout == ResponsiveLayoutMode.Compact;
        var isExtraWide = nextLayout == ResponsiveLayoutMode.ExtraWide;

        FilterColumn.Width = new GridLength(isExtraWide ? ExtraWideFilterPaneWidth : 0);
        HomeLayoutRoot.ColumnSpacing = isExtraWide || (_isEditorRequested && IsDockedEditorLayout(nextLayout)) ? 16 : 0;
        ResultsColumn.Width = new GridLength(1, GridUnitType.Star);
        ResultsPane.Width = double.NaN;
        ResultsPane.MaxWidth = double.PositiveInfinity;
        ResultsPane.HorizontalAlignment = HorizontalAlignment.Stretch;

        if (isExtraWide)
        {
            MoveFilterPaneToLayout();
        }
        else
        {
            MoveFilterPaneToFlyout();
        }

        UpdateEditorPanePlacement();
        ConfigureSearchToolbar(isCompact, isExtraWide);
        ConfigureStatisticsPanel(isExtraWide);
    }

    private static ResponsiveLayoutMode ResolveResponsiveLayout(double width, ResponsiveLayoutMode current)
    {
        return current switch
        {
            ResponsiveLayoutMode.ExtraWide when width >= ExtraWideLayoutExitWidth => ResponsiveLayoutMode.ExtraWide,
            ResponsiveLayoutMode.Wide when width >= ExtraWideLayoutEnterWidth => ResponsiveLayoutMode.ExtraWide,
            ResponsiveLayoutMode.Wide when width >= WideLayoutExitWidth => ResponsiveLayoutMode.Wide,
            ResponsiveLayoutMode.Medium when width >= ExtraWideLayoutEnterWidth => ResponsiveLayoutMode.ExtraWide,
            ResponsiveLayoutMode.Medium when width >= WideLayoutEnterWidth => ResponsiveLayoutMode.Wide,
            ResponsiveLayoutMode.Medium when width >= MediumLayoutMinimumWidth => ResponsiveLayoutMode.Medium,
            ResponsiveLayoutMode.Compact when width >= ExtraWideLayoutEnterWidth => ResponsiveLayoutMode.ExtraWide,
            ResponsiveLayoutMode.Compact when width >= WideLayoutEnterWidth => ResponsiveLayoutMode.Wide,
            ResponsiveLayoutMode.Compact when width >= MediumLayoutMinimumWidth => ResponsiveLayoutMode.Medium,
            _ when width >= ExtraWideLayoutEnterWidth => ResponsiveLayoutMode.ExtraWide,
            _ when width >= WideLayoutEnterWidth => ResponsiveLayoutMode.Wide,
            _ when width >= MediumLayoutMinimumWidth => ResponsiveLayoutMode.Medium,
            _ => ResponsiveLayoutMode.Compact,
        };
    }

    private void ConfigureSearchToolbar(bool isCompact, bool isExtraWide)
    {
        FilterButton.Visibility = isExtraWide ? Visibility.Collapsed : Visibility.Visible;
        SearchToolbarActionRow.Height = new GridLength(isCompact ? 1 : 0, isCompact ? GridUnitType.Auto : GridUnitType.Pixel);
        SearchInputColumn.Width = new GridLength(1, GridUnitType.Star);
        FilterButtonColumn.Width = new GridLength(1, GridUnitType.Auto);
        NewComponentButtonColumn.Width = new GridLength(1, GridUnitType.Auto);

        Grid.SetRow(SearchBox, 0);
        Grid.SetColumn(SearchBox, 0);
        Grid.SetColumnSpan(SearchBox, isCompact ? 3 : 1);
        Grid.SetRow(SearchToolbarActions, isCompact ? 1 : 0);
        Grid.SetColumn(SearchToolbarActions, isCompact ? 0 : 1);
        Grid.SetColumnSpan(SearchToolbarActions, isCompact ? 3 : 2);
        SearchToolbarActions.HorizontalAlignment = isCompact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void ConfigureStatisticsPanel(bool isExtraWide)
    {
        CompactStatsSummary.Visibility = Visibility.Visible;
        TotalCountCard.Visibility = Visibility.Collapsed;
        OwnedCountCard.Visibility = Visibility.Collapsed;
        CategoryCountCard.Visibility = Visibility.Collapsed;
    }

    private void MoveFilterPaneToFlyout()
    {
        if (ReferenceEquals(_filterFlyout.Content, FilterPane))
        {
            return;
        }

        HomeLayoutRoot.Children.Remove(FilterPane);
        _filterFlyout.Content = FilterPane;
    }

    private void MoveFilterPaneToLayout()
    {
        if (ReferenceEquals(_filterFlyout.Content, FilterPane))
        {
            if (_filterFlyout.IsOpen)
            {
                _restoreFilterPaneAfterFlyoutCloses = true;
                _filterFlyout.Hide();
                return;
            }

            _filterFlyout.Content = null;
        }

        HomeLayoutRoot.Children.Remove(FilterPane);
        Grid.SetColumn(FilterPane, 0);
        HomeLayoutRoot.Children.Add(FilterPane);
    }

    private void FilterFlyout_Closed(object? sender, object e)
    {
        if (!_restoreFilterPaneAfterFlyoutCloses)
        {
            return;
        }

        _restoreFilterPaneAfterFlyoutCloses = false;
        if (_responsiveLayout == ResponsiveLayoutMode.ExtraWide)
        {
            MoveFilterPaneToLayout();
        }
    }

    private void RequestEditor()
    {
        _isEditorRequested = true;
        UpdateEditorPanePlacement();
    }

    private static bool IsDockedEditorLayout(ResponsiveLayoutMode layout)
    {
        return layout is ResponsiveLayoutMode.Wide or ResponsiveLayoutMode.ExtraWide;
    }

    private void UpdateEditorPanePlacement()
    {
        var isDocked = IsDockedEditorLayout(_responsiveLayout);
        EditorColumn.Width = new GridLength(isDocked && _isEditorRequested ? DockedEditorPaneWidth : 0);
        EditorCloseButton.Visibility = isDocked && _isEditorRequested ? Visibility.Visible : Visibility.Collapsed;
        HomeLayoutRoot.ColumnSpacing = _responsiveLayout == ResponsiveLayoutMode.ExtraWide || (isDocked && _isEditorRequested) ? 16 : 0;

        if (!isDocked)
        {
            MoveEditorPaneToOverlay();
            return;
        }

        if (_isEditorOverlayOpen)
        {
            _editorOverlay?.Hide();
            return;
        }

        if (_isEditorRequested)
        {
            MoveEditorPaneToLayout();
        }
        else
        {
            MoveEditorPaneToOverlay();
        }
    }

    private void MoveEditorPaneToOverlay()
    {
        EnsureEditorOverlay();
        if (ReferenceEquals(_editorOverlay!.Content, EditorPane))
        {
            return;
        }

        HomeLayoutRoot.Children.Remove(EditorPane);
        _editorOverlay.Content = EditorPane;
    }

    private void MoveEditorPaneToLayout()
    {
        if (_isEditorOverlayOpen)
        {
            return;
        }

        if (ReferenceEquals(_editorOverlay?.Content, EditorPane))
        {
            _editorOverlay.Content = null;
        }

        HomeLayoutRoot.Children.Remove(EditorPane);
        Grid.SetColumn(EditorPane, 2);
        HomeLayoutRoot.Children.Add(EditorPane);
    }

    private void EnsureEditorOverlay()
    {
        _editorOverlay ??= new ContentDialog
        {
            CloseButtonText = "关闭",
            FullSizeDesired = true,
        };
    }

    private async Task ShowEditorOverlayIfNarrowAsync()
    {
        if (IsDockedEditorLayout(_responsiveLayout) || _isEditorOverlayOpen || XamlRoot is null)
        {
            return;
        }

        EnsureEditorOverlay();
        MoveEditorPaneToOverlay();
        _editorOverlay!.XamlRoot = XamlRoot;
        _isEditorOverlayOpen = true;
        try
        {
            await _editorOverlay.ShowAsync();
        }
        finally
        {
            _isEditorOverlayOpen = false;
            if (IsDockedEditorLayout(_responsiveLayout))
            {
                UpdateEditorPanePlacement();
            }
            else
            {
                _isEditorRequested = false;
            }
        }
    }

    private void ComponentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _pendingComponentGridWidth = e.NewSize.Width;
        _componentGridResizeTimer.Stop();
        _componentGridResizeTimer.Start();
    }

    private void ComponentGridResizeTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ConfigureComponentGridLayout(_pendingComponentGridWidth);
    }

    private void ConfigureComponentGridLayout(double contentWidth)
    {
        if (ComponentGrid.ItemsPanelRoot is not ItemsWrapGrid itemsWrapGrid)
        {
            return;
        }

        const double cardHeight = 112;
        const double cardSpacing = 12;
        const double minCardWidth = 280;
        const double verticalScrollbarReserve = 16;
        var availableWidth = Math.Max(0, contentWidth - verticalScrollbarReserve);
        if (availableWidth <= 0)
        {
            return;
        }

        var columns = (int)Math.Max(1, Math.Floor((availableWidth + cardSpacing) / (minCardWidth + cardSpacing)));
        var itemWidth = Math.Max(0, (availableWidth - (columns * cardSpacing)) / columns);

        itemsWrapGrid.ItemWidth = itemWidth;
        itemsWrapGrid.ItemHeight = cardHeight + cardSpacing;
        ComponentGrid.InvalidateMeasure();
    }

    private void NotifySidebarStateChanged()
    {
        SidebarStateChanged?.Invoke(this, EventArgs.Empty);
        RefreshLocalSidebar();
    }

    private bool _isSyncingLocalSidebar;

    private void RefreshLocalSidebar()
    {
        RunWhileSyncingLocalSidebar(RefreshSidebarFilterControls);
    }

    private void SyncSidebarFilterControls()
    {
        RunWhileSyncingLocalSidebar(RefreshSidebarFilterControls);
    }

    private void RunWhileSyncingLocalSidebar(Action action)
    {
        _isSyncingLocalSidebar = true;
        try
        {
            action();
        }
        finally
        {
            _isSyncingLocalSidebar = false;
        }
    }

    private void RefreshSidebarFilterControls()
            {
        if (SidebarMajorCategoryComboBox is not null)
                {
            SidebarMajorCategoryComboBox.SelectedValue = SelectedMajorCategoryId;
        }

        if (SidebarSubcategoryComboBox is not null)
        {
            SidebarSubcategoryComboBox.SelectedValue = SelectedSubCategoryId;
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

        if (SidebarAttributeTextBox is not null && !string.Equals(SidebarAttributeTextBox.Text, AttributeFilterText, StringComparison.Ordinal))
            {
                SidebarAttributeTextBox.Text = AttributeFilterText;
            }
        }

    private async void SidebarMajorCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        if (_isSyncingLocalSidebar)
        {
            return;
        }

        await ApplyMajorCategoryFilterAsync(SidebarMajorCategoryComboBox.SelectedValue as string);
    }

    private async void SidebarSubcategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingLocalSidebar)
        {
            return;
        }

        await ApplySubcategoryFilterAsync(SidebarSubcategoryComboBox.SelectedValue as string);
    }

    private void EditorMajorCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshEditorSubcategories();
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

    private void SidebarAttributeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingLocalSidebar)
        {
            return;
        }

        var nextAttributeText = SidebarAttributeTextBox.Text;
        if (string.Equals(AttributeFilterText, nextAttributeText, StringComparison.Ordinal))
        {
            return;
        }

        AttributeFilterText = nextAttributeText;
        QueueDebouncedFilterRefresh();
    }

    private enum ResponsiveLayoutMode
    {
        Unknown,
        Compact,
        Medium,
        Wide,
        ExtraWide,
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
