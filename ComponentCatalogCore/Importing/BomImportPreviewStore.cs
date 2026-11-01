namespace ComponentCatalogCore.Importing;

public sealed class BomImportPreviewStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredPreview> _previews = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly int _maximumCount;

    public BomImportPreviewStore(TimeSpan? lifetime = null, int maximumCount = 100, TimeProvider? timeProvider = null)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        _lifetime = lifetime ?? TimeSpan.FromMinutes(15);
        _maximumCount = maximumCount;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public BomImportPreview Create(IReadOnlyList<BomImportPreviewItem> items)
    {
        lock (_gate)
        {
            RemoveExpired();
            while (_previews.Count >= _maximumCount)
            {
                _previews.Remove(_previews.OrderBy(pair => pair.Value.ExpiresAt).First().Key);
            }

            var preview = new BomImportPreview { PreviewId = Guid.NewGuid().ToString("N"), Items = CloneItems(items) };
            _previews[preview.PreviewId] = new StoredPreview(Clone(preview), _timeProvider.GetUtcNow() + _lifetime);
            return Clone(preview);
        }
    }

    public BomImportPreview? Get(string previewId)
    {
        lock (_gate)
        {
            RemoveExpired();
            return _previews.TryGetValue(previewId, out var stored) ? Clone(stored.Preview) : null;
        }
    }

    private void RemoveExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var previewId in _previews.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToList())
        {
            _previews.Remove(previewId);
        }
    }

    private static BomImportPreview Clone(BomImportPreview preview) => new() { PreviewId = preview.PreviewId, Items = CloneItems(preview.Items) };

    private static IReadOnlyList<BomImportPreviewItem> CloneItems(IEnumerable<BomImportPreviewItem> items) => items.Select(item => new BomImportPreviewItem
    {
        RowIndex = item.RowIndex,
        Action = item.Action,
        Confidence = item.Confidence,
        Warnings = item.Warnings.ToList(),
        Record = CloneRecord(item.Record),
        MatchedComponentId = item.MatchedComponentId,
        CommitRecord = CloneRecord(item.CommitRecord),
    }).ToList();

    private static ComponentCatalogCore.Contracts.ComponentRecord CloneRecord(ComponentCatalogCore.Contracts.ComponentRecord record) => new()
    {
        Id = record.Id, Name = record.Name, PartNumber = record.PartNumber, Category = record.Category,
        MajorCategoryId = record.MajorCategoryId, SubCategoryId = record.SubCategoryId, Package = record.Package,
        Aliases = record.Aliases.ToList(), Keywords = record.Keywords.ToList(),
        Attributes = new Dictionary<string, string>(record.Attributes, StringComparer.OrdinalIgnoreCase), Owned = record.Owned,
        Location = record.Location, Substitutes = record.Substitutes.ToList(), Notes = record.Notes, SourceType = record.SourceType,
    };

    private sealed record StoredPreview(BomImportPreview Preview, DateTimeOffset ExpiresAt);
}
