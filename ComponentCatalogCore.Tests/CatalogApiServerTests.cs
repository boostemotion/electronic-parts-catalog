using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Transport;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogApiServerTests : IAsyncDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-api-tests-{Guid.NewGuid():N}");
    private readonly List<IAsyncDisposable> _asyncDisposables = [];

    [Fact]
    public async Task ApiServer_ExposesCategoriesSearchAndDetailEndpoints()
    {
        Directory.CreateDirectory(_workingDirectory);

        var repository = new CatalogRepository(
            new CatalogRepositoryOptions(
                Path.Combine(_workingDirectory, "catalog.db"),
                Path.Combine(_workingDirectory, "catalog.json")));
        _asyncDisposables.Add(repository);

        await repository.InitializeAsync();
        await repository.UpsertComponentAsync(new ComponentRecord
        {
            Id = "mcu-stm32",
            Name = "STM32F103C8T6",
            PartNumber = "STM32F103C8T6",
            Category = "MCU",
            Package = "LQFP48",
            Aliases = ["BluePill MCU"],
            Keywords = ["stm32", "cortex-m3"],
            Attributes = new Dictionary<string, string> { ["flash"] = "64KB" },
            Owned = true,
            Location = "Main A2",
            Substitutes = [],
            Notes = "controller",
            SourceType = ComponentSourceType.Manual,
        });

        await using var apiServer = await CatalogApiServer.StartAsync(repository);
        using var client = new HttpClient();

        var categoriesJson = await client.GetStringAsync($"{apiServer.BaseUrl}/categories");
        using var categoriesDoc = JsonDocument.Parse(categoriesJson);
        var categories = categoriesDoc.RootElement.GetProperty("categories").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["全部", "MCU"], categories);

        var searchJson = await client.GetStringAsync($"{apiServer.BaseUrl}/components/search?q=STM32F103C8T6");
        using var searchDoc = JsonDocument.Parse(searchJson);
        var result = Assert.Single(searchDoc.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal("mcu-stm32", result.GetProperty("component").GetProperty("id").GetString());

        var filteredJson = await client.GetStringAsync($"{apiServer.BaseUrl}/components?category=MCU&package=LQFP48&owned=true&attribute=flash=64KB");
        using var filteredDoc = JsonDocument.Parse(filteredJson);
        Assert.Single(filteredDoc.RootElement.GetProperty("results").EnumerateArray());

        var detailJson = await client.GetStringAsync($"{apiServer.BaseUrl}/components/mcu-stm32");
        using var detailDoc = JsonDocument.Parse(detailJson);
        Assert.Equal("STM32F103C8T6", detailDoc.RootElement.GetProperty("component").GetProperty("partNumber").GetString());
    }

    [Fact]
    public async Task ApiServer_AllowsLocalWriteDeleteAndCorsPreflight()
    {
        Directory.CreateDirectory(_workingDirectory);

        var repository = new CatalogRepository(
            new CatalogRepositoryOptions(
                Path.Combine(_workingDirectory, "catalog.db"),
                Path.Combine(_workingDirectory, "catalog.json")));
        _asyncDisposables.Add(repository);

        await repository.InitializeAsync();
        await using var apiServer = await CatalogApiServer.StartAsync(repository);
        using var client = new HttpClient();

        using var preflight = new HttpRequestMessage(new HttpMethod("OPTIONS"), $"{apiServer.BaseUrl}/components");
        using var preflightResponse = await client.SendAsync(preflight);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal("*", preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("GET", preflightResponse.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Contains("POST", preflightResponse.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Contains("DELETE", preflightResponse.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Contains("Content-Type", preflightResponse.Headers.GetValues("Access-Control-Allow-Headers").Single());

        var component = new ComponentRecord
        {
            Id = "api-ldo",
            Name = "API LDO",
            PartNumber = "API-LDO-33",
            Category = "电源",
            Package = "SOT23",
            Aliases = [],
            Keywords = ["ldo"],
            Attributes = new Dictionary<string, string> { ["voltage"] = "3.3V" },
            Owned = true,
            Location = "API A1",
            Substitutes = [],
            Notes = "created through api",
            SourceType = ComponentSourceType.Manual,
        };

        var createResponse = await client.PostAsync(
            $"{apiServer.BaseUrl}/components",
            new StringContent(JsonSerializer.Serialize(component, CatalogJson.DefaultOptions), System.Text.Encoding.UTF8, "application/json"));
        createResponse.EnsureSuccessStatusCode();

        var detailJson = await client.GetStringAsync($"{apiServer.BaseUrl}/components/api-ldo");
        using var detailDoc = JsonDocument.Parse(detailJson);
        Assert.Equal("API-LDO-33", detailDoc.RootElement.GetProperty("component").GetProperty("partNumber").GetString());

        var deleteResponse = await client.DeleteAsync($"{apiServer.BaseUrl}/components/api-ldo");
        deleteResponse.EnsureSuccessStatusCode();
        Assert.Null(await repository.GetByIdAsync("api-ldo"));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _asyncDisposables)
        {
            await disposable.DisposeAsync();
        }

        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }
}
