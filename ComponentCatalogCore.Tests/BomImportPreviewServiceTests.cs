using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Importing;
using ComponentCatalogCore.Persistence;

namespace ComponentCatalogCore.Tests;

public sealed class BomImportPreviewServiceTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"bom-preview-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreatePreviewAsync_NormalizesAndStoresImmutableProposal()
    {
        await using var repository = await CreateRepositoryAsync();
        var store = new BomImportPreviewStore();
        var service = new BomImportPreviewService(repository, store);

        var preview = await service.CreatePreviewAsync(new BomImportPreviewRequest
        {
            Rows =
            [
                new BomImportRow { Value = "100nf", Footprint = "C0603", Designators = ["C1"] },
            ],
        });

        var item = Assert.Single(preview.Items);
        Assert.NotEqual(string.Empty, preview.PreviewId);
        Assert.Equal(BomMatchAction.Create, item.Action);
        Assert.Equal("100nF", item.Record.PartNumber);
        Assert.Equal("0603", item.Record.Package);
        Assert.Contains("\"action\": \"create\"", System.Text.Json.JsonSerializer.Serialize(preview, ComponentCatalogCore.CatalogJson.DefaultOptions));

        item.Record.PartNumber = "mutated";
        var stored = Assert.IsType<BomImportPreview>(store.Get(preview.PreviewId));
        Assert.Equal("100nF", Assert.Single(stored.Items).Record.PartNumber);
    }

    [Fact]
    public async Task CreatePreviewAsync_LeavesReviewRowsNonCommittable()
    {
        await using var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(Component("existing", "AMS1117-3.3", "SOT89"));
        var service = new BomImportPreviewService(repository, new BomImportPreviewStore());

        var preview = await service.CreatePreviewAsync(new BomImportPreviewRequest
        {
            Rows =
            [
                new BomImportRow
                {
                    ManufacturerPart = "AMS1117-3.3",
                    Footprint = "SOT223",
                    Designators = ["U1"],
                },
            ],
        });

        Assert.Equal(BomMatchAction.Review, Assert.Single(preview.Items).Action);
        Assert.False(BomImportPreviewService.CanCommit(Assert.Single(preview.Items)));
    }

    [Fact]
    public async Task CreatePreviewAsync_RequiresReviewForUncategorizedRows()
    {
        var item = await CreatePreviewItemAsync(new BomImportRow
        {
            Value = "Widget",
            Footprint = "Custom",
            Category = "电容器",
        });

        Assert.Equal(BomMatchAction.Review, item.Action);
        Assert.NotEmpty(item.Warnings);
        Assert.False(BomImportPreviewService.CanCommit(item));
    }

    [Fact]
    public async Task CreatePreviewAsync_RequiresReviewWhenPartNumberIsMissing()
    {
        var item = await CreatePreviewItemAsync(new BomImportRow
        {
            Footprint = "C0603",
            Designators = ["C1"],
        });

        Assert.Equal(BomMatchAction.Review, item.Action);
        Assert.Contains(item.Warnings, warning => warning.Contains("型号", StringComparison.Ordinal));
        Assert.False(BomImportPreviewService.CanCommit(item));
    }

    [Fact]
    public async Task CreatePreviewAsync_RequiresReviewWhenPackageIsMissing()
    {
        var item = await CreatePreviewItemAsync(new BomImportRow
        {
            Value = "100nF",
            Designators = ["C1"],
        });

        Assert.Equal(BomMatchAction.Review, item.Action);
        Assert.Contains(item.Warnings, warning => warning.Contains("封装", StringComparison.Ordinal));
        Assert.False(BomImportPreviewService.CanCommit(item));
    }

    [Fact]
    public async Task CreatePreviewAsync_RequiresReviewForAnyWarning()
    {
        var item = await CreatePreviewItemAsync(new BomImportRow
        {
            Value = "100nF",
            Footprint = "C0603",
            Designators = ["C1"],
            Substitutes = ["unverified-alternative"],
        });

        Assert.Equal(BomMatchAction.Review, item.Action);
        Assert.Contains(item.Warnings, warning => warning.Contains("未验证", StringComparison.Ordinal));
        Assert.False(BomImportPreviewService.CanCommit(item));
    }

    [Fact]
    public async Task CreatePreviewAsync_LeavesClearPassiveRowCommittable()
    {
        var item = await CreatePreviewItemAsync(new BomImportRow
        {
            Value = "100nF",
            Footprint = "C0603",
            Designators = ["C1"],
        });

        Assert.Equal(BomMatchAction.Create, item.Action);
        Assert.Empty(item.Warnings);
        Assert.True(BomImportPreviewService.CanCommit(item));
    }

    [Fact]
    public async Task CreatePreviewAsync_CarriesBomQuantityAndDesignatorsIntoCommitRecord()
    {
        var item = await CreatePreviewItemAsync(new BomImportRow
        {
            Quantity = 4,
            Value = "100nF",
            Footprint = "C0603",
            Designators = ["C1", " C2 ", "c2"],
        });

        Assert.Equal(BomMatchAction.Create, item.Action);
        Assert.Equal(4, item.CommitRecord.Quantity);
        Assert.Equal(["C1", "C2"], item.CommitRecord.Designators);
    }

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<BomImportPreviewItem> CreatePreviewItemAsync(BomImportRow row)
    {
        await using var repository = await CreateRepositoryAsync();
        var service = new BomImportPreviewService(repository, new BomImportPreviewStore());
        var preview = await service.CreatePreviewAsync(new BomImportPreviewRequest { Rows = [row] });
        return Assert.Single(preview.Items);
    }

    private async Task<CatalogRepository> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_directory);
        var repository = new CatalogRepository(new CatalogRepositoryOptions(
            Path.Combine(_directory, "catalog.db"),
            Path.Combine(_directory, "catalog.json")));
        await repository.InitializeAsync();
        return repository;
    }

    private static ComponentRecord Component(string id, string partNumber, string package) => new()
    {
        Id = id,
        Name = partNumber,
        PartNumber = partNumber,
        Category = "电源",
        Package = package,
        Aliases = [],
        Keywords = [],
        Attributes = new Dictionary<string, string>(),
        Owned = true,
        Location = string.Empty,
        Substitutes = [],
        Notes = string.Empty,
        SourceType = ComponentSourceType.Manual,
    };
}
