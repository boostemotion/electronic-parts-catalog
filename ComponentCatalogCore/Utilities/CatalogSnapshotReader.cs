using System.Text.Json;

namespace ComponentCatalogCore.Utilities;

public static class CatalogSnapshotReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<CatalogSnapshotSummary> ReadSummaryAsync(string snapshotPath)
    {
        await using var stream = File.OpenRead(snapshotPath);
        var payload = await JsonSerializer.DeserializeAsync<CatalogSnapshotSummaryPayload>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot payload is empty.");

        return new CatalogSnapshotSummary
        {
            SnapshotPath = snapshotPath,
            ExportedAt = payload.Meta.ExportedAt,
            ComponentCount = payload.Meta.ComponentCount,
            Categories = payload.Meta.Categories,
        };
    }

    private sealed class CatalogSnapshotSummaryPayload
    {
        public required CatalogSnapshotSummaryMeta Meta { get; init; }
    }

    private sealed class CatalogSnapshotSummaryMeta
    {
        public required string ExportedAt { get; init; }
        public required int ComponentCount { get; init; }
        public required IReadOnlyList<string> Categories { get; init; }
    }
}

public sealed class CatalogSnapshotSummary
{
    public required string SnapshotPath { get; init; }
    public required string ExportedAt { get; init; }
    public required int ComponentCount { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
}
