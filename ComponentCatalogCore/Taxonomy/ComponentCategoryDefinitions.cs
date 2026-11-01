namespace ComponentCatalogCore.Taxonomy;

public sealed record ComponentCategoryDefinition(
    string Id,
    string Label,
    int Order,
    IReadOnlyList<ComponentSubcategoryDefinition> Subcategories);

public sealed record ComponentSubcategoryDefinition(
    string Id,
    string MajorCategoryId,
    string Label,
    int Order);

public sealed record ComponentCategoryTaxonomyDefinition(
    string Version,
    IReadOnlyList<ComponentCategoryDefinition> MajorCategories);

public sealed record ComponentCategoryAssignment(
    string MajorCategoryId,
    string SubCategoryId,
    string MajorCategory,
    string SubCategory);
