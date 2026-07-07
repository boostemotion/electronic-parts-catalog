using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComponentCatalogCore.Utilities;
using Microsoft.UI.Dispatching;

namespace ComponentCatalogWinUI.Services;

public sealed class CatalogRuntimeState : INotifyPropertyChanged
{
    private static readonly Lazy<CatalogRuntimeState> LazyCurrent = new(() => new CatalogRuntimeState());

    private string _databasePath = string.Empty;
    private string _snapshotPath = string.Empty;
    private string _apiBaseUrl = string.Empty;
    private string _exportedAt = string.Empty;
    private int _componentCount;
    private string _categoriesDisplay = "尚未加载";
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

    public string ApiStatusText => string.IsNullOrWhiteSpace(ApiBaseUrl)
        ? "未启动"
        : $"运行中 · {ApiBaseUrl}";

    public string LogDirectory => AppLogger.LogDirectory;

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

    public void UpdatePaths(string databasePath, string snapshotPath)
    {
        DatabasePath = databasePath;
        SnapshotPath = snapshotPath;
    }

    public void UpdateApiBaseUrl(string? apiBaseUrl)
    {
        ApiBaseUrl = apiBaseUrl ?? string.Empty;
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
            _dispatcherQueue.TryEnqueue(() => CatalogChanged?.Invoke(this, EventArgs.Empty));
            return;
        }

        CatalogChanged?.Invoke(this, EventArgs.Empty);
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
