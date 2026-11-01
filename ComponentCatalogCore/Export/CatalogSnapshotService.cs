using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Taxonomy;

namespace ComponentCatalogCore.Export;

public static class CatalogSnapshotService
{
    public static ExportCatalogSnapshot BuildSnapshot(IEnumerable<ComponentRecord> components, string exportedAt)
    {
        var materialized = components.ToList();
        var categories = materialized
            .Select(component => component.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCulture)
            .ToList();

        return new ExportCatalogSnapshot
        {
            Meta = new ExportCatalogMeta
            {
                ExportedAt = exportedAt,
                ComponentCount = materialized.Count,
                Categories = categories,
                TaxonomyVersion = ComponentCategoryTaxonomy.Version,
                MajorCategories = ComponentCategoryTaxonomy.MajorCategories,
            },
            Components = materialized,
        };
    }
}
