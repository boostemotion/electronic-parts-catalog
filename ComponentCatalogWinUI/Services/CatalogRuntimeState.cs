using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComponentCatalogCore;
using ComponentCatalogCore.Utilities;
using Microsoft.UI.Dispatching;

namespace ComponentCatalogWinUI.Services;

public sealed class CatalogRuntimeState : INotifyPropertyChanged
{
    private static readonly Lazy<CatalogRuntimeState> LazyCurrent = new(() => new CatalogRuntimeState());

    private string _databasePath = string.Empty;
    private string _snapshotPath = string.Empty;
    private string _apiBaseUrl = string.Empty;
    private string _localApiUrl = string.Empty;
    private string _lanApiUrl = string.Empty;
    private bool _lanEnabled;
    private string _selectedLanAddress = string.Empty;
    private IReadOnlyList<CatalogNetworkAddress> _availableLanAddresses = [];
    private string _apiStatusDetail = "未启动";
    private string _exportedAt = string.Empty;
    private int _componentCount;
    private string _categoriesDisplay = "尚未加载";
    private string _androidReleaseStatusText = "尚未检查 Android 安装包";
    private string _androidReleaseVersionText = "未发布";
    private string _androidReleaseIntegrityText = "未检查";
    private string _androidReleasePublishedAtText = "未知";
    private string _backupStatusText = "尚未检查备份";
    private readonly DispatcherQueue? _dispatcherQueue;

    private CatalogRuntimeState()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public static CatalogRuntimeState Current => LazyCurrent.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CatalogChanged;

    public string DatabasePath
    {
        get => _databasePath;
        private set => SetField(ref _databasePath, value);
    }

    public string SnapshotPath
    {
        get => _snapshotPath;
        private set => SetField(ref _snapshotPath, value);
    }

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        private set
        {
            if (SetField(ref _apiBaseUrl, value))
            {
                OnPropertyChanged(nameof(ApiStatusText));
            }
        }
    }

    public string LocalApiUrl
    {
        get => _localApiUrl;
        private set => SetField(ref _localApiUrl, value);
    }

    public string LanApiUrl
    {
        get => _lanApiUrl;
        private set => SetField(ref _lanApiUrl, value);
    }

    public bool LanEnabled
    {
        get => _lanEnabled;
        private set
        {
            if (SetField(ref _lanEnabled, value))
            {
                OnPropertyChanged(nameof(PhoneSyncStatusText));
            }
        }
    }

    public string SelectedLanAddress
    {
        get => _selectedLanAddress;
        private set
        {
            if (SetField(ref _selectedLanAddress, value))
            {
                OnPropertyChanged(nameof(PhoneNetworkText));
            }
        }
    }

    public IReadOnlyList<CatalogNetworkAddress> AvailableLanAddresses
    {
        get => _availableLanAddresses;
        private set => SetField(ref _availableLanAddresses, value);
    }

    public string ApiStatusText => string.IsNullOrWhiteSpace(LocalApiUrl)
        ? _apiStatusDetail
        : $"运行中 · {LocalApiUrl}";

    public string ApiStatusDetail => _apiStatusDetail;

    public string PhoneSyncStatusText => LanEnabled
        ? "已开放只读查询"
        : _apiStatusDetail.StartsWith("手机访问启动失败", StringComparison.Ordinal)
            ? _apiStatusDetail
            : "未开启";

    public string PhoneNetworkText => string.IsNullOrWhiteSpace(SelectedLanAddress)
        ? "未检测到可用私有网络"
        : SelectedLanAddress;

    public string LogDirectory => AppLogger.LogDirectory;

    public string ProductVersionText => $"v{ProductVersion.ProductVersionName}（Android code {ProductVersion.AndroidVersionCode}）";

    public string TaxonomyVersionText => $"v{ProductVersion.TaxonomyVersion}";

    public string ExportedAt
    {
        get => _exportedAt;
        private set
        {
            if (SetField(ref _exportedAt, value))
            {
                OnPropertyChanged(nameof(ExportedAtDisplay));
            }
        }
    }

    public string ExportedAtDisplay => string.IsNullOrWhiteSpace(ExportedAt) ? "尚未导出" : ExportedAt;

    public int ComponentCount
    {
        get => _componentCount;
        private set
        {
            if (SetField(ref _componentCount, value))
            {
                OnPropertyChanged(nameof(ComponentCountText));
            }
        }
    }

    public string CategoriesDisplay
    {
        get => _categoriesDisplay;
        private set => SetField(ref _categoriesDisplay, value);
    }

    public string ComponentCountText => $"元件数：{ComponentCount}";

    public string AndroidReleaseStatusText
    {
        get => _androidReleaseStatusText;
        private set => SetField(ref _androidReleaseStatusText, value);
    }

    public string AndroidReleaseVersionText
    {
        get => _androidReleaseVersionText;
        private set => SetField(ref _androidReleaseVersionText, value);
    }

    public string AndroidReleaseIntegrityText
    {
        get => _androidReleaseIntegrityText;
        private set => SetField(ref _androidReleaseIntegrityText, value);
    }

    public string AndroidReleasePublishedAtText
    {
        get => _androidReleasePublishedAtText;
        private set => SetField(ref _androidReleasePublishedAtText, value);
    }

    public string BackupStatusText
    {
        get => _backupStatusText;
        private set => SetField(ref _backupStatusText, value);
    }

    public void UpdateAndroidReleaseStatus(string status, string version, string integrity, string publishedAt)
    {
        AndroidReleaseStatusText = status;
        AndroidReleaseVersionText = version;
        AndroidReleaseIntegrityText = integrity;
        AndroidReleasePublishedAtText = publishedAt;
    }

    public void UpdateBackupStatus(string status) => BackupStatusText = status;

    public void UpdatePaths(string databasePath, string snapshotPath)
    {
        DatabasePath = databasePath;
        SnapshotPath = snapshotPath;
    }

    public void UpdateAvailableLanAddresses(IReadOnlyList<CatalogNetworkAddress> addresses, string? selectedAddress)
    {
        AvailableLanAddresses = addresses;
        SelectedLanAddress = selectedAddress ?? string.Empty;
    }

    public void UpdateApiEndpoints(string? localApiUrl, string? lanApiUrl, string? detail = null)
    {
        LocalApiUrl = localApiUrl ?? string.Empty;
        ApiBaseUrl = LocalApiUrl;
        LanApiUrl = lanApiUrl ?? string.Empty;
        LanEnabled = !string.IsNullOrWhiteSpace(LanApiUrl);
        _apiStatusDetail = detail ?? (string.IsNullOrWhiteSpace(LocalApiUrl) ? "未启动" : string.Empty);
        OnPropertyChanged(nameof(ApiStatusText));
        OnPropertyChanged(nameof(ApiStatusDetail));
        OnPropertyChanged(nameof(PhoneSyncStatusText));
    }

    public void UpdateApiBaseUrl(string? apiBaseUrl)
    {
        UpdateApiEndpoints(apiBaseUrl, null);
    }

    public void UpdateApiUnavailable(string error)
    {
        UpdateApiEndpoints(null, null, $"不可用 · {error}");
    }

    public async Task RefreshSnapshotSummaryAsync()
    {
        if (string.IsNullOrWhiteSpace(SnapshotPath) || !File.Exists(SnapshotPath))
        {
            ExportedAt = string.Empty;
            ComponentCount = 0;
            CategoriesDisplay = "尚未导出";
            return;
        }

        var summary = await CatalogSnapshotReader.ReadSummaryAsync(SnapshotPath);
        ExportedAt = summary.ExportedAt;
        ComponentCount = summary.ComponentCount;
        CategoriesDisplay = summary.Categories.Count == 0
            ? "暂无分类"
            : string.Join(" / ", summary.Categories);
    }

    public void NotifyCatalogChanged()
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => RaiseCatalogChanged());
            return;
        }

        RaiseCatalogChanged();
    }

    private void RaiseCatalogChanged()
    {
        CatalogChanged?.Invoke(this, EventArgs.Empty);

        // The catalog page listens through a single static entry point so that recreating the
        // page can never leave several listeners behind.
        ComponentCatalogWinUI.Pages.HomePage.NotifyActiveInstanceCatalogChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
