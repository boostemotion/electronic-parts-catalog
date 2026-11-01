using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Export;
using ComponentCatalogCore.Taxonomy;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogSemanticConsistencyVerifierTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-consistency-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task VerifyAsync_AcceptsCanonicalRecordsIgnoresExportedAtAndNormalizesLegacyManualSourceTypeCasing()
    {
        var (databasePath, snapshotPath) = await WriteFixtureAsync(exportedAt: "2099-12-31T23:59:59.0000000+00:00");

        var result = await CatalogSemanticConsistencyVerifier.VerifyAsync(databasePath, snapshotPath);

        Assert.True(result.IsConsistent, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("ok", result.DatabaseIntegrityCheck);
        Assert.Equal(1, result.DatabaseComponentCount);
        Assert.Equal(1, result.SnapshotComponentCount);
    }

    [Fact]
    public async Task VerifyAsync_ReportsEveryBusinessFieldAndMetadataMismatch()
    {
        var snapshotComponent = CreateComponent();
        snapshotComponent.Name = "Snapshot name";
        snapshotComponent.PartNumber = "SNAPSHOT-PART";
        snapshotComponent.Category = "Wrong category";
        snapshotComponent.MajorCategoryId = "passives";
        snapshotComponent.SubCategoryId = "mlcc";
        snapshotComponent.Package = "0805";
        snapshotComponent.Aliases = ["wrong alias"];
        snapshotComponent.Keywords = ["wrong keyword"];
        snapshotComponent.Attributes = new Dictionary<string, string> { ["voltage"] = "5V" };
        snapshotComponent.Owned = false;
        snapshotComponent.Location = "C2";
        snapshotComponent.Substitutes = ["wrong substitute"];
        snapshotComponent.Notes = "wrong notes";
        snapshotComponent.SourceType = ComponentSourceType.CsvImport;
        var (databasePath, snapshotPath) = await WriteFixtureAsync(
            snapshotComponent,
            componentCount: 2,
            categories: ["other"],
            taxonomyVersion: "wrong-taxonomy",
            majorCategories: []);

        var result = await CatalogSemanticConsistencyVerifier.VerifyAsync(databasePath, snapshotPath);

        Assert.False(result.IsConsistent);
        foreach (var field in new[]
                 {
                     "name", "partNumber", "category", "majorCategoryId", "subCategoryId", "package", "aliases", "keywords",
                     "attributes", "owned", "location", "substitutes", "notes", "sourceType",
                 })
        {
            Assert.Contains(result.Errors, error => error.Contains($"component-1.{field}", StringComparison.Ordinal));
        }

        Assert.Contains(result.Errors, error => error.Contains("meta.componentCount", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("meta.categories", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("meta.taxonomyVersion", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("meta.majorCategories count", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingAndUnexpectedIds()
    {
        var snapshotComponent = CreateComponent();
        snapshotComponent.Id = "snapshot-only";
        var (databasePath, snapshotPath) = await WriteFixtureAsync(snapshotComponent);

        var result = await CatalogSemanticConsistencyVerifier.VerifyAsync(databasePath, snapshotPath);

        Assert.False(result.IsConsistent);
        Assert.Contains(result.Errors, error => error.Contains("missing from catalog.json: component-1", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("not present in SQLite: snapshot-only", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private async Task<(string DatabasePath, string SnapshotPath)> WriteFixtureAsync(
        ComponentRecord? snapshotComponent = null,
        int componentCount = 1,
        IReadOnlyList<string>? categories = null,
        string? taxonomyVersion = null,
        IReadOnlyList<ComponentCategoryDefinition>? majorCategories = null,
        string exportedAt = "2026-07-13T00:00:00.0000000+00:00")
    {
        Directory.CreateDirectory(_workingDirectory);
        var databasePath = Path.Combine(_workingDirectory, "catalog.db");
        var snapshotPath = Path.Combine(_workingDirectory, "catalog.json");
        var databaseComponent = CreateComponent();

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE components (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    part_number TEXT NOT NULL,
                    category TEXT NOT NULL,
                    major_category_id TEXT NOT NULL,
                    sub_category_id TEXT NOT NULL,
                    package_name TEXT NOT NULL,
                    aliases_json TEXT NOT NULL,
                    keywords_json TEXT NOT NULL,
                    attributes_json TEXT NOT NULL,
                    owned INTEGER NOT NULL,
                    location TEXT NOT NULL,
                    substitutes_json TEXT NOT NULL,
                    notes TEXT NOT NULL,
                    source_type TEXT NOT NULL
                );
                INSERT INTO components (
                    id, name, part_number, category, major_category_id, sub_category_id, package_name,
                    aliases_json, keywords_json, attributes_json, owned, location, substitutes_json, notes, source_type
                ) VALUES (
                    $id, $name, $partNumber, $category, $majorCategoryId, $subCategoryId, $packageName,
                    $aliases, $keywords, $attributes, $owned, $location, $substitutes, $notes, 'Manual'
                );
                """;
            command.Parameters.AddWithValue("$id", databaseComponent.Id);
            command.Parameters.AddWithValue("$name", databaseComponent.Name);
            command.Parameters.AddWithValue("$partNumber", databaseComponent.PartNumber);
            command.Parameters.AddWithValue("$category", databaseComponent.Category);
            command.Parameters.AddWithValue("$majorCategoryId", databaseComponent.MajorCategoryId);
            command.Parameters.AddWithValue("$subCategoryId", databaseComponent.SubCategoryId);
            command.Parameters.AddWithValue("$packageName", databaseComponent.Package);
            command.Parameters.AddWithValue("$aliases", JsonSerializer.Serialize(databaseComponent.Aliases));
            command.Parameters.AddWithValue("$keywords", JsonSerializer.Serialize(databaseComponent.Keywords));
            command.Parameters.AddWithValue("$attributes", JsonSerializer.Serialize(databaseComponent.Attributes));
            command.Parameters.AddWithValue("$owned", databaseComponent.Owned ? 1 : 0);
            command.Parameters.AddWithValue("$location", databaseComponent.Location);
            command.Parameters.AddWithValue("$substitutes", JsonSerializer.Serialize(databaseComponent.Substitutes));
            command.Parameters.AddWithValue("$notes", databaseComponent.Notes);
            await command.ExecuteNonQueryAsync();
        }

        var snapshot = new ExportCatalogSnapshot
        {
            Meta = new ExportCatalogMeta
            {
                ExportedAt = exportedAt,
                ComponentCount = componentCount,
                Categories = categories ?? [databaseComponent.Category],
                TaxonomyVersion = taxonomyVersion ?? ComponentCategoryTaxonomy.Version,
                MajorCategories = majorCategories ?? ComponentCategoryTaxonomy.MajorCategories,
            },
            Components = [snapshotComponent ?? CreateComponent()],
        };
        await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, CatalogJson.DefaultOptions));
        return (databasePath, snapshotPath);
    }

    private static ComponentRecord CreateComponent() => new()
    {
        Id = "component-1",
        Name = "Database name",
        PartNumber = "PART-1",
        Category = "线性稳压器(LDO)",
        MajorCategoryId = "protection-power",
        SubCategoryId = "ldo",
        Package = "SOT223",
        Aliases = ["1117", "LDO"],
        Keywords = ["power", "3.3V"],
        Attributes = new Dictionary<string, string> { ["voltage"] = "3.3V", ["current"] = "1A" },
        Owned = true,
        Location = "B1",
        Substitutes = ["ME6211"],
        Notes = "fixture",
        SourceType = ComponentSourceType.Manual,
    };
}
