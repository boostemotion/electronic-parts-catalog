using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Export;
using ComponentCatalogCore.Utilities;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogSnapshotReaderTests
{
    [Fact]
    public async Task ReadSummaryAsync_ReturnsSnapshotMetaAndCategories()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-snapshot-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var snapshotPath = Path.Combine(workingDirectory, "catalog.json");
            var snapshot = CatalogSnapshotService.BuildSnapshot(
                [
                    new ComponentRecord
                    {
                        Id = "ldo-ams1117",
                        Name = "AMS1117-3.3",
                        PartNumber = "AMS1117-3.3",
                        Category = "电源",
                        Package = "SOT223",
                        Aliases = ["1117 LDO"],
                        Keywords = ["ldo", "3.3v"],
                        Attributes = new Dictionary<string, string> { ["voltage"] = "3.3V" },
                        Owned = true,
                        Location = "B1",
                        Substitutes = ["ME6211"],
                        Notes = "snapshot",
                        SourceType = ComponentSourceType.Manual,
                    },
                ],
                "2026-07-04T06:00:00.0000000+00:00");

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
            await File.WriteAllTextAsync(snapshotPath, json);

            var summary = await CatalogSnapshotReader.ReadSummaryAsync(snapshotPath);

            Assert.Equal(snapshotPath, summary.SnapshotPath);
            Assert.Equal("2026-07-04T06:00:00.0000000+00:00", summary.ExportedAt);
            Assert.Equal(1, summary.ComponentCount);
            Assert.Equal(["电源"], summary.Categories);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
