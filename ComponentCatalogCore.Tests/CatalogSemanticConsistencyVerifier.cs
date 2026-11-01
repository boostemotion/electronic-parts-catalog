using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Export;
using ComponentCatalogCore.Taxonomy;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Tests;

public static class CatalogSemanticConsistencyVerifier
{
    public static async Task<CatalogConsistencyVerificationResult> VerifyAsync(string databasePath, string snapshotPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        var errors = new List<string>();
        var databaseComponents = await ReadDatabaseComponentsAsync(databasePath, cancellationToken);
        var integrityCheck = await ReadIntegrityCheckAsync(databasePath, cancellationToken);
        var snapshot = await ReadSnapshotAsync(snapshotPath, cancellationToken);

        if (!string.Equals(integrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"SQLite integrity_check returned '{integrityCheck}'.");
        }

        CompareMeta(databaseComponents, snapshot, errors);
        CompareComponents(databaseComponents, snapshot.Components, errors);

        return new CatalogConsistencyVerificationResult(errors.Count == 0, integrityCheck, databaseComponents.Count, snapshot.Components.Count, errors);
    }

    private static async Task<IReadOnlyList<ComponentRecord>> ReadDatabaseComponentsAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, part_number, major_category_id, sub_category_id, package_name,
                   aliases_json, keywords_json, attributes_json, owned, location, substitutes_json, notes, source_type
            FROM components
            ORDER BY id COLLATE BINARY;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var components = new List<ComponentRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            components.Add(new ComponentRecord
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                PartNumber = reader.GetString(reader.GetOrdinal("part_number")),
                Category = ComponentCategoryTaxonomy.GetAssignment(reader.GetString(reader.GetOrdinal("major_category_id")), reader.GetString(reader.GetOrdinal("sub_category_id"))).SubCategory,
                MajorCategoryId = reader.GetString(reader.GetOrdinal("major_category_id")),
                SubCategoryId = reader.GetString(reader.GetOrdinal("sub_category_id")),
                Package = reader.GetString(reader.GetOrdinal("package_name")),
                Aliases = DeserializeList(reader.GetString(reader.GetOrdinal("aliases_json"))),
                Keywords = DeserializeList(reader.GetString(reader.GetOrdinal("keywords_json"))),
                Attributes = DeserializeDictionary(reader.GetString(reader.GetOrdinal("attributes_json"))),
                Owned = reader.GetInt32(reader.GetOrdinal("owned")) == 1,
                Location = reader.GetString(reader.GetOrdinal("location")),
                Substitutes = DeserializeList(reader.GetString(reader.GetOrdinal("substitutes_json"))),
                Notes = reader.GetString(reader.GetOrdinal("notes")),
                SourceType = ComponentSourceTypeContract.Parse(reader.GetString(reader.GetOrdinal("source_type"))),
            });
        }

        return components;
    }

    private static async Task<string> ReadIntegrityCheckAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return (string)(await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty);
    }

    private static async Task<ExportCatalogSnapshot> ReadSnapshotAsync(string snapshotPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(Path.GetFullPath(snapshotPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
        var snapshot = await JsonSerializer.DeserializeAsync<ExportCatalogSnapshot>(stream, CatalogJson.DefaultOptions, cancellationToken);
        return snapshot ?? throw new InvalidDataException("catalog.json did not contain a snapshot.");
    }

    private static void CompareMeta(IReadOnlyList<ComponentRecord> databaseComponents, ExportCatalogSnapshot snapshot, List<string> errors)
    {
        var expected = CatalogSnapshotService.BuildSnapshot(databaseComponents, exportedAt: string.Empty).Meta;
        if (snapshot.Meta.ComponentCount != databaseComponents.Count)
        {
            errors.Add($"meta.componentCount differs: SQLite has {databaseComponents.Count}, catalog.json has {snapshot.Meta.ComponentCount}.");
        }

        if (snapshot.Components.Count != databaseComponents.Count)
        {
            errors.Add($"components count differs: SQLite has {databaseComponents.Count}, catalog.json has {snapshot.Components.Count}.");
        }

        CompareList("meta.categories", expected.Categories, snapshot.Meta.Categories, errors);
        CompareValue("meta.taxonomyVersion", expected.TaxonomyVersion, snapshot.Meta.TaxonomyVersion, errors);
        CompareTaxonomy(expected.MajorCategories, snapshot.Meta.MajorCategories, errors);
    }

    private static void CompareComponents(IReadOnlyList<ComponentRecord> databaseComponents, IReadOnlyList<ComponentRecord> snapshotComponents, List<string> errors)
    {
        var databaseById = databaseComponents.ToDictionary(component => component.Id, StringComparer.Ordinal);
        foreach (var id in snapshotComponents.GroupBy(component => component.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add($"catalog.json contains duplicate component ID: {id}.");
        }

        var snapshotById = snapshotComponents.GroupBy(component => component.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var id in databaseById.Keys.Except(snapshotById.Keys, StringComparer.Ordinal))
        {
            errors.Add($"component ID missing from catalog.json: {id}.");
        }

        foreach (var id in snapshotById.Keys.Except(databaseById.Keys, StringComparer.Ordinal))
        {
            errors.Add($"catalog.json component ID not present in SQLite: {id}.");
        }

        foreach (var id in databaseById.Keys.Intersect(snapshotById.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            CompareComponent(id, Canonicalize(databaseById[id]), Canonicalize(snapshotById[id]), errors);
        }
    }

    private static ComponentRecord Canonicalize(ComponentRecord component) => new()
    {
        Id = component.Id,
        Name = component.Name,
        PartNumber = component.PartNumber,
        Category = component.Category,
        MajorCategoryId = component.MajorCategoryId,
        SubCategoryId = component.SubCategoryId,
        Package = component.Package,
        Aliases = component.Aliases,
        Keywords = component.Keywords,
        Attributes = component.Attributes,
        Owned = component.Owned,
        Location = component.Location,
        Substitutes = component.Substitutes,
        Notes = component.Notes,
        SourceType = ComponentSourceTypeContract.Parse(ComponentSourceTypeContract.ToContractValue(component.SourceType)),
    };

    private static void CompareComponent(string id, ComponentRecord database, ComponentRecord snapshot, List<string> errors)
    {
        CompareValue($"component {id}.id", database.Id, snapshot.Id, errors);
        CompareValue($"component {id}.name", database.Name, snapshot.Name, errors);
        CompareValue($"component {id}.partNumber", database.PartNumber, snapshot.PartNumber, errors);
        CompareValue($"component {id}.category", database.Category, snapshot.Category, errors);
        CompareValue($"component {id}.majorCategoryId", database.MajorCategoryId, snapshot.MajorCategoryId, errors);
        CompareValue($"component {id}.subCategoryId", database.SubCategoryId, snapshot.SubCategoryId, errors);
        CompareValue($"component {id}.package", database.Package, snapshot.Package, errors);
        CompareList($"component {id}.aliases", database.Aliases, snapshot.Aliases, errors);
        CompareList($"component {id}.keywords", database.Keywords, snapshot.Keywords, errors);
        CompareDictionary($"component {id}.attributes", database.Attributes, snapshot.Attributes, errors);
        CompareValue($"component {id}.owned", database.Owned, snapshot.Owned, errors);
        CompareValue($"component {id}.location", database.Location, snapshot.Location, errors);
        CompareList($"component {id}.substitutes", database.Substitutes, snapshot.Substitutes, errors);
        CompareValue($"component {id}.notes", database.Notes, snapshot.Notes, errors);
        CompareValue($"component {id}.sourceType", database.SourceType, snapshot.SourceType, errors);
    }

    private static void CompareTaxonomy(IReadOnlyList<ComponentCategoryDefinition> expected, IReadOnlyList<ComponentCategoryDefinition> actual, List<string> errors)
    {
        if (expected.Count != actual.Count)
        {
            errors.Add($"meta.majorCategories count differs: expected {expected.Count}, catalog.json has {actual.Count}.");
            return;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedMajor = expected[index];
            var actualMajor = actual[index];
            var prefix = $"meta.majorCategories[{index}]";
            CompareValue($"{prefix}.id", expectedMajor.Id, actualMajor.Id, errors);
            CompareValue($"{prefix}.label", expectedMajor.Label, actualMajor.Label, errors);
            CompareValue($"{prefix}.order", expectedMajor.Order, actualMajor.Order, errors);
            if (expectedMajor.Subcategories.Count != actualMajor.Subcategories.Count)
            {
                errors.Add($"{prefix}.subcategories count differs: expected {expectedMajor.Subcategories.Count}, catalog.json has {actualMajor.Subcategories.Count}.");
                continue;
            }

            for (var subcategoryIndex = 0; subcategoryIndex < expectedMajor.Subcategories.Count; subcategoryIndex++)
            {
                var expectedSubcategory = expectedMajor.Subcategories[subcategoryIndex];
                var actualSubcategory = actualMajor.Subcategories[subcategoryIndex];
                var subcategoryPrefix = $"{prefix}.subcategories[{subcategoryIndex}]";
                CompareValue($"{subcategoryPrefix}.id", expectedSubcategory.Id, actualSubcategory.Id, errors);
                CompareValue($"{subcategoryPrefix}.majorCategoryId", expectedSubcategory.MajorCategoryId, actualSubcategory.MajorCategoryId, errors);
                CompareValue($"{subcategoryPrefix}.label", expectedSubcategory.Label, actualSubcategory.Label, errors);
                CompareValue($"{subcategoryPrefix}.order", expectedSubcategory.Order, actualSubcategory.Order, errors);
            }
        }
    }

    private static void CompareValue<T>(string name, T expected, T actual, List<string> errors)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            errors.Add($"{name} differs: SQLite '{expected}', catalog.json '{actual}'.");
        }
    }

    private static void CompareList(string name, IReadOnlyList<string> expected, IReadOnlyList<string> actual, List<string> errors)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            errors.Add($"{name} differs: SQLite [{string.Join(", ", expected)}], catalog.json [{string.Join(", ", actual)}].");
        }
    }

    private static void CompareDictionary(string name, IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual, List<string> errors)
    {
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal)))
        {
            errors.Add($"{name} differs after JSON parsing.");
        }
    }

    private static IReadOnlyList<string> DeserializeList(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? throw new InvalidDataException("A component array field was null.");

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? throw new InvalidDataException("A component attributes field was null.");
}

public sealed record CatalogConsistencyVerificationResult(bool IsConsistent, string DatabaseIntegrityCheck, int DatabaseComponentCount, int SnapshotComponentCount, IReadOnlyList<string> Errors);
