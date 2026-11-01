using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Storage;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogBackupServiceTests : IAsyncLifetime
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-backup-tests-{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _snapshotPath;
    private readonly string _backupRoot;

    public CatalogBackupServiceTests()
    {
        _databasePath = Path.Combine(_workingDirectory, "Data", "catalog.db");
        _snapshotPath = Path.Combine(_workingDirectory, "Data", "catalog.json");
        _backupRoot = Path.Combine(_workingDirectory, "Backups");
    }

    [Fact]
    public async Task CreateAsync_CreatesValidatedTimestampedBackupWithMetadataAndHashes()
    {
        await SaveCatalogAsync("original", "ORIGINAL");
        var service = CreateService();

        var backup = await service.CreateAsync();
        var validation = await service.ValidateAsync(backup.DirectoryPath);

        Assert.True(Directory.Exists(backup.DirectoryPath));
        Assert.Matches("^catalog-\\d{8}-\\d{6}-", Path.GetFileName(backup.DirectoryPath));
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(1, backup.Metadata.ComponentCount);
        Assert.Equal("1.1-test", backup.Metadata.AppVersion);
        Assert.Equal("taxonomy-test", backup.Metadata.TaxonomyVersion);
        Assert.Equal("ok", backup.Metadata.IntegrityResult);
        Assert.True(File.Exists(Path.Combine(backup.DirectoryPath, "catalog.db")));
        Assert.True(File.Exists(Path.Combine(backup.DirectoryPath, "catalog.json")));
        Assert.True(File.Exists(Path.Combine(backup.DirectoryPath, "metadata.json")));
        Assert.False(string.IsNullOrWhiteSpace(backup.Metadata.DatabaseSha256));
        Assert.False(string.IsNullOrWhiteSpace(backup.Metadata.SnapshotSha256));
    }

    [Fact]
    public async Task CreateAsync_RegeneratesConsistentSnapshotWhenCurrentSnapshotIsMissing()
    {
        await SaveCatalogAsync("original", "ORIGINAL");
        File.Delete(_snapshotPath);

        var backup = await CreateService().CreateAsync();

        Assert.True(backup.Validation.IsValid, backup.Validation.Error);
        Assert.True(File.Exists(Path.Combine(backup.DirectoryPath, "catalog.json")));
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestBackupsWithValidationStatusAndAppliesRetention()
    {
        await SaveCatalogAsync("original", "ORIGINAL");
        var service = CreateService(maxBackups: 2);

        var first = await service.CreateAsync();
        await Task.Delay(5);
        var second = await service.CreateAsync();
        await Task.Delay(5);
        var third = await service.CreateAsync();

        var backups = await service.ListAsync();

        Assert.Equal(2, backups.Count);
        Assert.DoesNotContain(backups, backup => backup.DirectoryPath == first.DirectoryPath);
        Assert.Equal(third.DirectoryPath, backups[0].DirectoryPath);
        Assert.Equal(second.DirectoryPath, backups[1].DirectoryPath);
        Assert.All(backups, backup => Assert.True(backup.Validation.IsValid, backup.Validation.Error));
    }

    [Fact]
    public async Task ValidateAsync_RejectsBackupWhenAHashDoesNotMatch()
    {
        await SaveCatalogAsync("original", "ORIGINAL");
        var service = CreateService();
        var backup = await service.CreateAsync();
        await File.AppendAllTextAsync(Path.Combine(backup.DirectoryPath, "catalog.json"), "tampered");

        var validation = await service.ValidateAsync(backup.DirectoryPath);

        Assert.False(validation.IsValid);
        Assert.Contains("hash", validation.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreAsync_ValidatesCandidateCreatesSafetyBackupAndRestoresCatalog()
    {
        await SaveCatalogAsync("backup-source", "BACKUP-SOURCE");
        var service = CreateService();
        var candidate = await service.CreateAsync();
        await SaveCatalogAsync("current", "CURRENT");
        var lifecycle = new TestRestoreLifecycle(_databasePath, _snapshotPath);

        var result = await service.RestoreAsync(candidate.DirectoryPath, lifecycle);
        var restored = await ReadPartNumberAsync();
        var backups = await service.ListAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("BACKUP-SOURCE", restored);
        Assert.Equal(1, lifecycle.StopCalls);
        Assert.Equal(1, lifecycle.ReinitializeCalls);
        Assert.Equal(1, lifecycle.RegenerateSnapshotCalls);
        Assert.Contains(backups, backup => backup.Metadata.Reason == "pre-restore" && backup.Validation.IsValid);
    }

    [Fact]
    public async Task RestoreAsync_DoesNotRollbackSuccessfulRestoreWhenRetentionCleanupFails()
    {
        await SaveCatalogAsync("backup-source", "BACKUP-SOURCE");
        var service = CreateService(maxBackups: 2);
        var candidate = await service.CreateAsync();
        var obsolete = await service.CreateAsync();
        await SaveCatalogAsync("current", "CURRENT");
        await using var lockStream = new FileStream(
            Path.Combine(obsolete.DirectoryPath, "metadata.json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var result = await service.RestoreAsync(candidate.DirectoryPath, new TestRestoreLifecycle(_databasePath, _snapshotPath));

        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.Error);
        Assert.Equal("BACKUP-SOURCE", await ReadPartNumberAsync());
    }

    [Fact]
    public async Task CreateAsync_ReturnsValidatedBackupWithWarningWhenRetentionCleanupFails()
    {
        await SaveCatalogAsync("original", "ORIGINAL");
        var service = CreateService(maxBackups: 1);
        var obsolete = await service.CreateAsync();
        await Task.Delay(5);
        await using var lockStream = new FileStream(
            Path.Combine(obsolete.DirectoryPath, "metadata.json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var backup = await service.CreateAsync();

        Assert.True(backup.Validation.IsValid, backup.Validation.Error);
        Assert.NotNull(backup.RetentionWarning);
        Assert.Contains("retention cleanup failed", backup.RetentionWarning, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(backup.DirectoryPath));
    }

    [Fact]
    public async Task RestoreAsync_RollsBackSafetyBackupWhenPostRestoreRegenerationFails()
    {
        await SaveCatalogAsync("backup-source", "BACKUP-SOURCE");
        var service = CreateService();
        var candidate = await service.CreateAsync();
        await SaveCatalogAsync("current", "CURRENT");
        var lifecycle = new TestRestoreLifecycle(_databasePath, _snapshotPath, failFirstSnapshotRegeneration: true);

        var exception = await Assert.ThrowsAsync<CatalogRestoreException>(() => service.RestoreAsync(candidate.DirectoryPath, lifecycle));

        Assert.Equal("CURRENT", await ReadPartNumberAsync());
        Assert.True(exception.RollbackSucceeded, exception.Message);
        Assert.Equal(1, lifecycle.StopCalls);
        Assert.Equal(2, lifecycle.ReinitializeCalls);
        Assert.Equal(2, lifecycle.RegenerateSnapshotCalls);
        Assert.Contains(await service.ListAsync(), backup => backup.Metadata.Reason == "pre-restore" && backup.Validation.IsValid);
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_workingDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private CatalogBackupService CreateService(int maxBackups = 8) => new(new CatalogBackupOptions(
        _databasePath,
        _snapshotPath,
        _backupRoot,
        "1.1-test",
        "taxonomy-test",
        maxBackups));

    private async Task SaveCatalogAsync(string id, string partNumber)
    {
        await using var repository = new CatalogRepository(new CatalogRepositoryOptions(_databasePath, _snapshotPath));
        await repository.InitializeAsync();
        var existing = await repository.ListComponentsAsync();
        foreach (var component in existing)
        {
            await repository.DeleteByIdAsync(component.Id);
        }

        await repository.SaveComponentAsync(new ComponentRecord
        {
            Id = id,
            Name = partNumber,
            PartNumber = partNumber,
            Category = "其他",
            MajorCategoryId = "other",
            SubCategoryId = "uncategorized",
            Package = "0603",
            Aliases = [],
            Keywords = [],
            Attributes = new Dictionary<string, string>(),
            Owned = true,
            Location = "A1",
            Substitutes = [],
            Notes = "backup test",
            SourceType = ComponentSourceType.Manual,
        });
    }

    private async Task<string> ReadPartNumberAsync()
    {
        await using var repository = new CatalogRepository(new CatalogRepositoryOptions(_databasePath, _snapshotPath));
        await repository.InitializeAsync();
        return Assert.Single(await repository.ListComponentsAsync()).PartNumber;
    }

    private sealed class TestRestoreLifecycle : ICatalogRestoreLifecycle
    {
        private readonly string _databasePath;
        private readonly string _snapshotPath;
        private readonly bool _failFirstSnapshotRegeneration;

        public TestRestoreLifecycle(string databasePath, string snapshotPath, bool failFirstSnapshotRegeneration = false)
        {
            _databasePath = databasePath;
            _snapshotPath = snapshotPath;
            _failFirstSnapshotRegeneration = failFirstSnapshotRegeneration;
        }

        public int StopCalls { get; private set; }
        public int ReinitializeCalls { get; private set; }
        public int RegenerateSnapshotCalls { get; private set; }

        public Task StopAsync()
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task ReinitializeAsync()
        {
            ReinitializeCalls++;
            return Task.CompletedTask;
        }

        public async Task RegenerateSnapshotAsync()
        {
            RegenerateSnapshotCalls++;
            if (_failFirstSnapshotRegeneration && RegenerateSnapshotCalls == 1)
            {
                throw new InvalidOperationException("forced snapshot regeneration failure");
            }

            await using var repository = new CatalogRepository(new CatalogRepositoryOptions(_databasePath, _snapshotPath));
            await repository.InitializeAsync();
            await repository.ExportSnapshotAsync();
        }
    }
}
