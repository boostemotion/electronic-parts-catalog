using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Taxonomy;

namespace ComponentCatalogCore.Validation;

public static class ComponentRecordValidator
{
    public const int MaximumBodyFieldLength = 512;
    public const int MaximumNotesLength = 4_096;
    public const int MaximumCollectionEntries = 100;

    public static string? ValidateAndNormalize(ComponentRecord component)
    {
        component.Id = (component.Id ?? string.Empty).Trim();
        component.Name = (component.Name ?? string.Empty).Trim();
        component.PartNumber = (component.PartNumber ?? string.Empty).Trim();
        component.MajorCategoryId = (component.MajorCategoryId ?? string.Empty).Trim();
        component.SubCategoryId = (component.SubCategoryId ?? string.Empty).Trim();
        component.Category = (component.Category ?? string.Empty).Trim();
        component.Package = (component.Package ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(component.MajorCategoryId) && string.IsNullOrEmpty(component.SubCategoryId))
        {
            var legacyAssignment = ComponentCategoryTaxonomy.ResolveLegacy(component.Category, component.Package, component.PartNumber);
            if (legacyAssignment is null)
            {
                return "Category must resolve to a known taxonomy entry.";
            }

            component.MajorCategoryId = legacyAssignment.MajorCategoryId;
            component.SubCategoryId = legacyAssignment.SubCategoryId;
        }
        else if (!ComponentCategoryTaxonomy.IsValidPair(component.MajorCategoryId, component.SubCategoryId))
        {
            return "MajorCategoryId and SubCategoryId must be a known taxonomy pair.";
        }

        component.Category = ComponentCategoryTaxonomy.GetAssignment(component.MajorCategoryId, component.SubCategoryId).SubCategory;
        component.Location = (component.Location ?? string.Empty).Trim();
        component.Notes = (component.Notes ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(component.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(component.PartNumber))
        {
            return "PartNumber is required.";
        }

        if (!HasValidLength(component.Id, MaximumBodyFieldLength) ||
            !HasValidLength(component.Name, MaximumBodyFieldLength) ||
            !HasValidLength(component.PartNumber, MaximumBodyFieldLength) ||
            !HasValidLength(component.Category, MaximumBodyFieldLength) ||
            !HasValidLength(component.Package, MaximumBodyFieldLength) ||
            !HasValidLength(component.Location, MaximumBodyFieldLength) ||
            !HasValidLength(component.Notes, MaximumNotesLength))
        {
            return "One or more text fields exceed the supported length.";
        }

        if (!HasValidCollection(component.Aliases) ||
            !HasValidCollection(component.Keywords) ||
            !HasValidCollection(component.Substitutes) ||
            !HasValidAttributes(component.Attributes))
        {
            return "Collections contain invalid or too many entries.";
        }

        return null;
    }

    private static bool HasValidLength(string value, int maximumLength) => value.Length <= maximumLength;

    private static bool HasValidCollection(IReadOnlyList<string>? values) =>
        values is not null &&
        values.Count <= MaximumCollectionEntries &&
        values.All(value => value is not null && value.Length <= MaximumBodyFieldLength);

    private static bool HasValidAttributes(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is not null &&
        attributes.Count <= MaximumCollectionEntries &&
        attributes.All(pair => pair.Key is not null && pair.Value is not null &&
                               pair.Key.Length <= MaximumBodyFieldLength &&
                               pair.Value.Length <= MaximumBodyFieldLength);
}
