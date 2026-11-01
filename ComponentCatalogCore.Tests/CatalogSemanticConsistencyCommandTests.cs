namespace ComponentCatalogCore.Tests;

[Trait("Category", "ManualCatalogConsistency")]
public sealed class CatalogSemanticConsistencyCommandTests
{
    [Fact]
    public async Task VerifyProvidedPathsAsync()
    {
        var databasePath = Environment.GetEnvironmentVariable("CATALOG_CONSISTENCY_DATABASE");
        var snapshotPath = Environment.GetEnvironmentVariable("CATALOG_CONSISTENCY_SNAPSHOT");
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(snapshotPath))
        {
            Console.WriteLine("No catalog consistency paths were supplied; use scripts/verify-catalog-consistency.ps1.");
            return;
        }

        var result = await CatalogSemanticConsistencyVerifier.VerifyAsync(databasePath, snapshotPath);
        Console.WriteLine($"SQLite integrity_check: {result.DatabaseIntegrityCheck}; SQLite components: {result.DatabaseComponentCount}; catalog.json components: {result.SnapshotComponentCount}; differences: {result.Errors.Count}.");
        Assert.True(result.IsConsistent, string.Join(Environment.NewLine, result.Errors));
    }
}
