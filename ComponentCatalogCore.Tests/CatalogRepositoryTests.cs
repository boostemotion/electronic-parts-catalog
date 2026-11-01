using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Persistence;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogRepositoryTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-core-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Repository_PersistsComponentsAndExportsCatalogSnapshot()
    {
        await using var repository = await CreateRepositoryAsync();

        await repository.SaveComponentAsync(CreateComponent("mcu-stm32", "STM32F103C8T6"));

        var components = await repository.ListComponentsAsync();
        var component = Assert.Single(components);
        Assert.Equal("STM32F103C8T6", component.PartNumber);

        var searchResults = await repository.SearchAsync(new SearchQuery
        {
            Text = "STM32F103C8T6",
            Category = "全部",
            Owned = true,
        });
        Assert.Single(searchResults);

        var snapshotPath = Path.Combine(_workingDirectory, "catalog.json");
        Assert.True(File.Exists(snapshotPath));
        var snapshotText = await File.ReadAllTextAsync(snapshotPath);
        Assert.Contains("STM32F103C8T6", snapshotText);
        Assert.Contains("\"componentCount\": 1", snapshotText);
        Assert.Contains("\"sourceType\": \"manual\"", snapshotText);
    }

    [Fact]
    public async Task Repository_PersistsBomQuantityAndDesignators()
    {
        await using var repository = await CreateRepositoryAsync();
        var component = CreateComponent("cap-0603", "100nF");
        component.Quantity = 3;
        component.Designators = ["C1", " C2 ", "c2"];

        await repository.SaveComponentAsync(component);

        var saved = Assert.Single(await repository.ListComponentsAsync());
        Assert.Equal(3, saved.Quantity);
        Assert.Equal(["C1", "C2"], saved.Designators);
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_workingDirectory, "catalog.json")));
        var serialized = snapshot.RootElement.GetProperty("components")[0];
        Assert.Equal(3, serialized.GetProperty("quantity").GetInt32());
        Assert.Equal(
            ["C1", "C2"],
            serialized.GetProperty("designators").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task SaveComponentAsync_UpdatesExistingIdWhenPartNumberChanges()
    {
        await using var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("component-1", "OLD-PART"));

        await repository.SaveComponentAsync(CreateComponent("component-1", "NEW-PART", name: "Updated component"));

        var components = await repository.ListComponentsAsync();
        var saved = Assert.Single(components);
        Assert.Equal("component-1", saved.Id);
        Assert.Equal("NEW-PART", saved.PartNumber);
        Assert.Equal("Updated component", saved.Name);
        Assert.True(File.Exists(Path.Combine(_workingDirectory, "catalog.json")));
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_workingDirectory, "catalog.json")));
        Assert.Equal(1, snapshot.RootElement.GetProperty("meta").GetProperty("componentCount").GetInt32());
    }

    [Fact]
    public async Task SaveComponentAsync_DuplicatePartNumberPreservesBothRows()
    {
        await using var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("component-1", "PART-ONE"));
        await repository.SaveComponentAsync(CreateComponent("component-2", "PART-TWO"));

        var exception = await Assert.ThrowsAsync<DuplicatePartNumberException>(() =>
            repository.SaveComponentAsync(CreateComponent("component-1", "PART-TWO")));

        Assert.Equal("PART-TWO", exception.PartNumber);
        var components = await repository.ListComponentsAsync();
        Assert.Equal(2, components.Count);
        Assert.Equal("PART-ONE", (await repository.GetByIdAsync("component-1"))!.PartNumber);
        Assert.Equal("PART-TWO", (await repository.GetByIdAsync("component-2"))!.PartNumber);
    }

    [Fact]
    public async Task SaveComponentAsync_UsesPartNumberAndPackageAsTheIdentity()
    {
        await using var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("resistor-0603", "10k", package: "0603"));
        await repository.SaveComponentAsync(CreateComponent("resistor-0805", "10k", package: "0805"));

        var duplicate = await Assert.ThrowsAsync<DuplicatePartNumberException>(() =>
            repository.SaveComponentAsync(CreateComponent("other-resistor-0603", "10k", package: "0603")));

        Assert.Equal("10k", duplicate.PartNumber);
        Assert.Equal("0603", duplicate.Package);
        Assert.Equal(2, (await repository.ListComponentsAsync()).Count);
        Assert.Equal("resistor-0603", (await repository.GetByPartNumberAndPackageAsync("10k", "0603"))!.Id);
        Assert.Equal("resistor-0805", (await repository.GetByPartNumberAndPackageAsync("10k", "0805"))!.Id);
        Assert.NotNull(await repository.GetByPartNumberAsync("10k"));
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacyPartNumberUniquenessWithoutLosingRows()
    {
        Directory.CreateDirectory(_workingDirectory);
        var databasePath = Path.Combine(_workingDirectory, "catalog.db");
        await CreateLegacyCatalogDatabaseAsync(databasePath);

        await using var repository = new CatalogRepository(new CatalogRepositoryOptions(
            databasePath,
            Path.Combine(_workingDirectory, "catalog.json")));
        await repository.InitializeAsync();
        await repository.SaveComponentAsync(CreateComponent("resistor-0805", "10k", package: "0805"));

        var components = await repository.ListComponentsAsync();
        Assert.Equal(2, components.Count);
        var legacy = Assert.Single(components, component => component.Id == "legacy-resistor-0603");
        Assert.Equal("10k", legacy.PartNumber);
        Assert.Equal("0603", legacy.Package);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = "SELECT COUNT(*) FROM pragma_index_list('components') WHERE name = 'ux_components_part_number_package' AND \"unique\" = 1";
        Assert.Equal(1L, await indexCommand.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Repository_SerializesConcurrentOperationsAndInitialization()
    {
        await using var repository = CreateRepository();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.InitializeAsync()));

        var writes = Enumerable.Range(0, 24)
            .Select(index => repository.SaveComponentAsync(CreateComponent($"component-{index}", $"PART-{index}")));
        var reads = Enumerable.Range(0, 24)
            .Select(_ => repository.ListComponentsAsync());
        await Task.WhenAll(writes.Concat(reads));

        var components = await repository.ListComponentsAsync();
        Assert.Equal(24, components.Count);
    }

    [Fact]
    public async Task ImportCsvWithSummaryAsync_ReportsInsertedSkippedOverwrittenAndMergedCounts()
    {
        await using var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("existing-ldo", "AMS1117-3.3", name: "老款 LDO", package: "SOT223"));

        const string csv = """
name,partNumber,category,package,owned,aliases,keywords,attributes
新版 LDO,AMS1117-3.3,电源,SOT223,true,1117,ldo|3.3v,voltage=3.3V
10k 电阻,RES-10K-0603-1%,电阻,0603,true,10k,10k|0603,resistance=10k
""";

        var skip = await repository.ImportCsvWithSummaryAsync(csv, DuplicateStrategy.Skip);
        Assert.Equal(1, skip.InsertedCount);
        Assert.Equal(1, skip.SkippedCount);
        Assert.Equal(0, skip.OverwrittenCount);
        Assert.Equal(0, skip.MergedCount);
        Assert.Equal(2, skip.ValidRowCount);
        Assert.Equal(0, skip.ErrorCount);

        var merge = await repository.ImportCsvWithSummaryAsync(csv, DuplicateStrategy.MergeAlias);
        Assert.Equal(0, merge.InsertedCount);
        Assert.Equal(0, merge.SkippedCount);
        Assert.Equal(0, merge.OverwrittenCount);
        Assert.Equal(2, merge.MergedCount);
        Assert.Contains("1117", (await repository.GetByPartNumberAsync("AMS1117-3.3"))!.Aliases);

        var overwrite = await repository.ImportCsvWithSummaryAsync(csv, DuplicateStrategy.Overwrite);
        Assert.Equal(0, overwrite.InsertedCount);
        Assert.Equal(0, overwrite.SkippedCount);
        Assert.Equal(2, overwrite.OverwrittenCount);
        Assert.Equal("新版 LDO", (await repository.GetByPartNumberAsync("AMS1117-3.3"))!.Name);
    }

    [Fact]
    public async Task ImportCsvWithSummaryAsync_RollsBackAllRowsWhenDatabaseMutationFails()
    {
        await using var repository = await CreateRepositoryAsync();
        var databasePath = Path.Combine(_workingDirectory, "catalog.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER reject_broken_import
                BEFORE INSERT ON components
                WHEN NEW.part_number = 'BROKEN-PART'
                BEGIN
                    SELECT RAISE(ABORT, 'forced import failure');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        const string csv = """
name,partNumber,category,package,owned
Good,GOOD-PART,其他,0603,true
Broken,BROKEN-PART,其他,0603,true
""";

        await Assert.ThrowsAsync<SqliteException>(() => repository.ImportCsvWithSummaryAsync(csv, DuplicateStrategy.Skip));
        Assert.Empty(await repository.ListComponentsAsync());
    }

    [Fact]
    public async Task ExportSnapshotAsync_RemainsParseableAndLeavesNoTemporaryFiles()
    {
        await using var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("component-1", "PART-1"));

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repository.ExportSnapshotAsync()));

        var snapshotPath = Path.Combine(_workingDirectory, "catalog.json");
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.Equal(1, snapshot.RootElement.GetProperty("meta").GetProperty("componentCount").GetInt32());
        Assert.Empty(Directory.GetFiles(_workingDirectory, "catalog.json.*.tmp"));
    }

    [Fact]
    public async Task SaveComponentsAsync_SavesAllComponentsInOneBatch()
    {
        await using var repository = await CreateRepositoryAsync();

        await repository.SaveComponentsAsync(
        [
            CreateComponent("batch-one", "BATCH-ONE"),
            CreateComponent("batch-two", "BATCH-TWO"),
        ]);

        Assert.Equal(2, (await repository.ListComponentsAsync()).Count);
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_workingDirectory, "catalog.json")));
        Assert.Equal(2, snapshot.RootElement.GetProperty("meta").GetProperty("componentCount").GetInt32());
    }

    [Fact]
    public async Task SaveComponentsAsync_RollsBackAllComponentsWhenOneSaveFails()
    {
        await using var repository = await CreateRepositoryAsync();
        var databasePath = Path.Combine(_workingDirectory, "catalog.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER reject_broken_batch
                BEFORE INSERT ON components
                WHEN NEW.part_number = 'BROKEN-BATCH'
                BEGIN
                    SELECT RAISE(ABORT, 'forced batch failure');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() => repository.SaveComponentsAsync(
        [
            CreateComponent("batch-good", "BATCH-GOOD"),
            CreateComponent("batch-broken", "BROKEN-BATCH"),
        ]));

        Assert.Empty(await repository.ListComponentsAsync());
    }

    [Fact]
    public async Task Repository_SavesBomNormalizedRecord()
    {
        await using var repository = await CreateRepositoryAsync();
        var normalized = Importing.BomComponentNormalizer.Normalize(new Importing.BomImportRow
        {
            Value = "100nf",
            Footprint = "C0603",
            Designators = ["C1"],
        });

        await repository.SaveComponentsAsync([normalized.Record]);

        Assert.Equal("mlcc", Assert.Single(await repository.ListComponentsAsync()).SubCategoryId);
    }

    [Fact]
    public async Task InitializeAsync_AddsCategoryColumnsWithoutRewritingLegacyRows()
    {
        Directory.CreateDirectory(_workingDirectory);
        var databasePath = Path.Combine(_workingDirectory, "catalog.db");
        await CreateLegacyCatalogDatabaseAsync(databasePath);

        await using var repository = new CatalogRepository(new CatalogRepositoryOptions(databasePath, Path.Combine(_workingDirectory, "catalog.json")));
        await repository.InitializeAsync();
        await repository.InitializeAsync();

        var legacy = Assert.Single(await repository.ListComponentsAsync());
        Assert.Equal("other", legacy.MajorCategoryId);
        Assert.Equal("uncategorized", legacy.SubCategoryId);
        Assert.Equal("未分类", legacy.Category);

        await repository.SaveComponentAsync(new ComponentRecord
        {
            Id = "ldo",
            Name = "AMS1117",
            PartNumber = "AMS1117-3.3",
            Category = "ignored display value",
            MajorCategoryId = "protection-power",
            SubCategoryId = "ldo",
            Package = "SOT223",
            Aliases = [],
            Keywords = [],
            Attributes = new Dictionary<string, string>(),
            Substitutes = [],
            SourceType = ComponentSourceType.Manual,
        });

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT major_category_id, sub_category_id FROM components WHERE id = 'ldo'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("protection-power", reader.GetString(0));
        Assert.Equal("ldo", reader.GetString(1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private CatalogRepository CreateRepository()
    {
        Directory.CreateDirectory(_workingDirectory);
        return new CatalogRepository(new CatalogRepositoryOptions(
            Path.Combine(_workingDirectory, "catalog.db"),
            Path.Combine(_workingDirectory, "catalog.json")));
    }

    private async Task<CatalogRepository> CreateRepositoryAsync()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task CreateLegacyCatalogDatabaseAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE components (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                part_number TEXT NOT NULL UNIQUE,
                category TEXT NOT NULL,
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
                id, name, part_number, category, package_name, aliases_json, keywords_json,
                attributes_json, owned, location, substitutes_json, notes, source_type
            ) VALUES (
                'legacy-resistor-0603', '10k', '10k', '测试', '0603', '[]', '[]', '{}', 1, 'A1', '[]', 'legacy row', 'manual'
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static ComponentRecord CreateComponent(string id, string partNumber, string? name = null, string package = "0603")
    {
        return new ComponentRecord
        {
            Id = id,
            Name = name ?? partNumber,
            PartNumber = partNumber,
            Category = "其他",
            Package = package,
            Aliases = [],
            Keywords = [],
            Attributes = new Dictionary<string, string>(),
            Owned = true,
            Location = "A1",
            Substitutes = [],
            Notes = "test",
            SourceType = ComponentSourceType.Manual,
        };
    }
}
