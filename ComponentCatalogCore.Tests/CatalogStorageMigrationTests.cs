using ComponentCatalogCore.Storage;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogStorageMigrationTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-migration-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PathResolver_UsesLocalAppDataByDefaultAndKeepsCandidateOrderDeterministic()
    {
        var root = CatalogStoragePathResolver.ResolveRootDirectory(@"C:\Users\Test\AppData\Local", null, "ComponentCatalogWinUI");
        var overridden = CatalogStoragePathResolver.ResolveRootDirectory(@"C:\Users\Test\AppData\Local", @"D:\Catalog", "ComponentCatalogWinUI");
        var candidates = CatalogStoragePathResolver.GetMigrationCandidateDirectories(@"C:\Users\Test\AppData\Local", "ComponentCatalogWinUI", @"G:\LegacyWorkspace\入库");

        Assert.Equal(@"C:\Users\Test\AppData\Local\ComponentCatalogWinUI", root);
        Assert.Equal(Path.GetFullPath(@"D:\Catalog"), overridden);
        Assert.Equal(@"C:\Users\Test\AppData\Local\ComponentCatalogWinUI\CatalogData", candidates[0]);
        Assert.Equal(@"G:\LegacyWorkspace\入库\ComponentCatalogData", candidates[1]);
    }

    [Fact]
    public async Task MigrateDatabaseAsync_CopiesValidatedDatabaseAndPreservesSource()
    {
        Directory.CreateDirectory(_workingDirectory);
        var sourcePath = Path.Combine(_workingDirectory, "legacy.db");
        var destinationPath = Path.Combine(_workingDirectory, "new", "catalog.db");
        await CreateDatabaseAsync(sourcePath);

        var result = await CatalogStorageMigration.MigrateDatabaseAsync(sourcePath, destinationPath);

        Assert.True(result.Succeeded);
        Assert.True(result.Attempted);
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(destinationPath));
        await using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        await destination.OpenAsync();
        await using var command = destination.CreateCommand();
        command.CommandText = "SELECT name FROM components WHERE id = 'preserved'";
        Assert.Equal("Preserved component", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MigrateDatabaseAsync_FailureLeavesSourceAndDestinationStateForRetry()
    {
        Directory.CreateDirectory(_workingDirectory);
        var sourcePath = Path.Combine(_workingDirectory, "invalid.db");
        var destinationPath = Path.Combine(_workingDirectory, "new", "catalog.db");
        await File.WriteAllTextAsync(sourcePath, "not a sqlite database");

        var failed = await CatalogStorageMigration.MigrateDatabaseAsync(sourcePath, destinationPath);
        Assert.False(failed.Succeeded);
        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(destinationPath));

        File.Delete(sourcePath);
        await CreateDatabaseAsync(sourcePath);
        var retry = await CatalogStorageMigration.MigrateDatabaseAsync(sourcePath, destinationPath);
        Assert.True(retry.Succeeded);
        Assert.True(File.Exists(destinationPath));
    }

    [Fact]
    public async Task MigrateSnapshotAsync_RejectsMalformedSnapshotWithoutPromotion()
    {
        Directory.CreateDirectory(_workingDirectory);
        var sourcePath = Path.Combine(_workingDirectory, "legacy.json");
        var destinationPath = Path.Combine(_workingDirectory, "new", "catalog.json");
        await File.WriteAllTextAsync(sourcePath, "{");

        var failed = await CatalogStorageMigration.MigrateSnapshotAsync(sourcePath, destinationPath);
        Assert.False(failed.Succeeded);
        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(destinationPath));

        await File.WriteAllTextAsync(sourcePath, """{"meta":{"exportedAt":"2026-07-10T00:00:00Z"},"components":[]}""");
        var retry = await CatalogStorageMigration.MigrateSnapshotAsync(sourcePath, destinationPath);
        Assert.True(retry.Succeeded);
        Assert.True(File.Exists(destinationPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private static async Task CreateDatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE components (id TEXT PRIMARY KEY, name TEXT NOT NULL);
            INSERT INTO components (id, name) VALUES ('preserved', 'Preserved component');
            """;
        await command.ExecuteNonQueryAsync();
    }
}
