using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Persistence;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogRepositoryTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-core-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Repository_PersistsComponentsAndExportsCatalogSnapshot()
    {
        Directory.CreateDirectory(_workingDirectory);

        var options = new CatalogRepositoryOptions(
            Path.Combine(_workingDirectory, "catalog.db"),
            Path.Combine(_workingDirectory, "catalog.json"));

        await using var repository = new CatalogRepository(options);
        await repository.InitializeAsync();

        await repository.UpsertComponentAsync(new ComponentRecord
        {
            Id = "mcu-stm32",
            Name = "STM32F103C8T6",
            PartNumber = "STM32F103C8T6",
            Category = "MCU",
            Package = "LQFP48",
            Aliases = ["BluePill MCU"],
            Keywords = ["cortex-m3"],
            Attributes = new Dictionary<string, string> { ["flash"] = "64KB" },
            Owned = true,
            Location = "Main A2",
            Substitutes = [],
            Notes = "controller",
            SourceType = ComponentSourceType.Manual,
        });

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
    public async Task ImportCsvWithSummaryAsync_ReportsInsertedSkippedOverwrittenAndMergedCounts()
    {
        Directory.CreateDirectory(_workingDirectory);

        var options = new CatalogRepositoryOptions(
            Path.Combine(_workingDirectory, "catalog.db"),
            Path.Combine(_workingDirectory, "catalog.json"));

        await using var repository = new CatalogRepository(options);
        await repository.InitializeAsync();
        await repository.UpsertComponentAsync(new ComponentRecord
        {
            Id = "existing-ldo",
            Name = "老款 LDO",
            PartNumber = "AMS1117-3.3",
            Category = "电源",
            Package = "SOT223",
            Aliases = ["老料"],
            Keywords = [],
            Attributes = new Dictionary<string, string>(),
            Owned = true,
            Location = "A1",
            Substitutes = [],
            Notes = "existing",
            SourceType = ComponentSourceType.Manual,
        });

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

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }
}
