using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Taxonomy;

namespace ComponentCatalogCore.Importing;

public sealed class BomImportPreviewService
{
    private readonly CatalogRepository _repository;
    private readonly BomImportPreviewStore _store;

    public BomImportPreviewService(CatalogRepository repository, BomImportPreviewStore store)
    {
        _repository = repository;
        _store = store;
    }

    public async Task<BomImportPreview> CreatePreviewAsync(BomImportPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await _repository.ListComponentsAsync();
        var items = request.Rows.Select((row, index) => CreateItem(row, index, existing)).ToList();
        return _store.Create(items);
    }

    public static bool CanCommit(BomImportPreviewItem item) =>
        item.Action is BomMatchAction.Create or BomMatchAction.Merge &&
        item.Warnings.Count == 0 &&
        item.Record.MajorCategoryId != ComponentCategoryTaxonomy.OtherMajorCategoryId &&
        item.Record.SubCategoryId != ComponentCategoryTaxonomy.UncategorizedSubCategoryId &&
        !string.IsNullOrWhiteSpace(item.Record.PartNumber) &&
        !string.IsNullOrWhiteSpace(item.Record.Package);

    private static BomImportPreviewItem CreateItem(BomImportRow row, int rowIndex, IReadOnlyList<ComponentRecord> existing)
    {
        var normalized = BomComponentNormalizer.Normalize(row);
        var match = BomComponentMatcher.Match(normalized.Record, !string.IsNullOrWhiteSpace(row.ManufacturerPart), existing);
        var warnings = normalized.Warnings.Concat(match.Warnings).Distinct(StringComparer.Ordinal).ToList();

        AddEligibilityWarnings(normalized.Record, warnings);
        var requiresReview = warnings.Count > 0 ||
            normalized.Record.MajorCategoryId == ComponentCategoryTaxonomy.OtherMajorCategoryId ||
            normalized.Record.SubCategoryId == ComponentCategoryTaxonomy.UncategorizedSubCategoryId ||
            string.IsNullOrWhiteSpace(normalized.Record.PartNumber) ||
            string.IsNullOrWhiteSpace(normalized.Record.Package);

        var commitRecord = match.Action == BomMatchAction.Merge && match.MatchedComponent is not null
            ? match.MatchedComponent
            : normalized.Record;
        return new BomImportPreviewItem
        {
            RowIndex = rowIndex,
            Action = requiresReview ? BomMatchAction.Review : match.Action,
            Confidence = match.Confidence,
            Warnings = warnings,
            Record = normalized.Record,
            MatchedComponentId = match.MatchedComponent?.Id,
            CommitRecord = commitRecord,
        };
    }

    private static void AddEligibilityWarnings(ComponentRecord record, ICollection<string> warnings)
    {
        if (record.MajorCategoryId == ComponentCategoryTaxonomy.OtherMajorCategoryId ||
            record.SubCategoryId == ComponentCategoryTaxonomy.UncategorizedSubCategoryId)
        {
            AddWarning(warnings, "分类为 other/uncategorized，不能提交。");
        }

        if (string.IsNullOrWhiteSpace(record.PartNumber))
        {
            AddWarning(warnings, "缺少可确认的型号或值，不能提交。");
        }

        if (string.IsNullOrWhiteSpace(record.Package))
        {
            AddWarning(warnings, "缺少可确认的封装，不能提交。");
        }
    }

    private static void AddWarning(ICollection<string> warnings, string warning)
    {
        if (!warnings.Contains(warning, StringComparer.Ordinal))
        {
            warnings.Add(warning);
        }
    }
}
