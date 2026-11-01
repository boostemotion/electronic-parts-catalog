using System.Security.Cryptography;
using System.Text.Json;
using ComponentCatalogCore.Persistence;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Storage;

public sealed record CatalogBackupOptions(
    string DatabasePath,
    string SnapshotPath,
    string BackupRootPath,
    string AppVersion,
    string TaxonomyVersion,
    int MaxBackups = 12);

public sealed record CatalogBackupMetadata(
    string CreatedUtc,
    int ComponentCount,
    string DatabaseSha256,
    string SnapshotSha256,
    string IntegrityResult,
    string AppVersion,
    string TaxonomyVersion,
    string Reason);

public sealed record CatalogBackupValidation(bool IsValid, string? Error, CatalogBackupMetadata? Metadata = null);

public sealed record CatalogBackupInfo(
    string DirectoryPath,
    CatalogBackupMetadata Metadata,
    CatalogBackupValidation Validation,
    string? RetentionWarning = null);

public sealed record CatalogRestoreResult(
    bool Succeeded,
    string BackupDirectoryPath,
    string SafetyBackupDirectoryPath,
    string? Error = null);

public interface ICatalogRestoreLifecycle
{
    Task StopAsync();
    Task ReinitializeAsync();
    Task RegenerateSnapshotAsync();
}

public sealed class CatalogRestoreException : InvalidOperationException
{
    public CatalogRestoreException(string message, bool rollbackSucceeded, Exception innerException)
        : base(message, innerException)
    {
        RollbackSucceeded = rollbackSucceeded;
    }

    public bool RollbackSucceeded { get; }
}

public sealed class CatalogBackupService
{
    private const string DatabaseFileName = "catalog.db";
    private const string SnapshotFileName = "catalog.json";
    private const string MetadataFileName = "metadata.json";
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly CatalogBackupOptions _options;

    public CatalogBackupService(CatalogBackupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DatabasePath) ||
            string.IsNullOrWhiteSpace(options.SnapshotPath) ||
            string.IsNullOrWhiteSpace(options.BackupRootPath))
        {
            throw new ArgumentException("Catalog database, snapshot, and backup paths are required.", nameof(options));
        }

        if (options.MaxBackups < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "At least one backup must be retained.");
        }

        _options = options with
        {
            DatabasePath = Path.GetFullPath(options.DatabasePath),
            SnapshotPath = Path.GetFullPath(options.SnapshotPath),
            BackupRootPath = Path.GetFullPath(options.BackupRootPath),
        };
    }

    public async Task<CatalogBackupInfo> CreateAsync(string reason = "manual")
    {
        var backup = await CreateCoreAsync(reason);
        var retentionWarning = await TryApplyRetentionAsync(backup.DirectoryPath);
        return backup with { RetentionWarning = retentionWarning };
    }

    public async Task<IReadOnlyList<CatalogBackupInfo>> ListAsync()
    {
        if (!Directory.Exists(_options.BackupRootPath))
        {
            return [];
        }

        var backups = new List<CatalogBackupInfo>();
        foreach (var directoryPath in Directory.EnumerateDirectories(_options.BackupRootPath, "catalog-*", SearchOption.TopDirectoryOnly))
        {
            var validation = await ValidateAsync(directoryPath);
            backups.Add(new CatalogBackupInfo(
                directoryPath,
                validation.Metadata ?? CreateUnavailableMetadata(directoryPath),
                validation));
        }

        return backups
            .OrderByDescending(backup => backup.Metadata.CreatedUtc, StringComparer.Ordinal)
            .ThenByDescending(backup => backup.DirectoryPath, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<CatalogBackupValidation> ValidateAsync(string backupDirectoryPath)
    {
        try
        {
            var directoryPath = GetBackupDirectoryPath(backupDirectoryPath);
            if (!Directory.Exists(directoryPath))
            {
                return Invalid("Backup directory does not exist.");
            }

            var metadataPath = Path.Combine(directoryPath, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                return Invalid("Backup metadata is missing.");
            }

            var metadata = await ReadMetadataAsync(metadataPath);
            ValidateMetadata(metadata);

            var databasePath = Path.Combine(directoryPath, DatabaseFileName);
            var snapshotPath = Path.Combine(directoryPath, SnapshotFileName);
            if (!File.Exists(databasePath) || !File.Exists(snapshotPath))
            {
                return Invalid("Backup database or snapshot is missing.", metadata);
            }

            if (!await HashMatchesAsync(databasePath, metadata.DatabaseSha256) ||
                !await HashMatchesAsync(snapshotPath, metadata.SnapshotSha256))
            {
                return Invalid("Backup file hash does not match metadata.", metadata);
            }

            var databaseSummary = await GetDatabaseSummaryAsync(databasePath);
            if (!string.Equals(databaseSummary.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return Invalid($"SQLite integrity check failed: {databaseSummary.IntegrityResult}", metadata);
            }

            if (databaseSummary.ComponentCount != metadata.ComponentCount)
            {
                return Invalid("Backup component count does not match the database.", metadata);
            }

            var snapshotCount = await GetSnapshotComponentCountAsync(snapshotPath);
            if (snapshotCount != metadata.ComponentCount)
            {
                return Invalid("Backup component count does not match the snapshot.", metadata);
            }

            return new CatalogBackupValidation(true, null, metadata);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or SqliteException or ArgumentException)
        {
            return Invalid(exception.Message);
        }
    }

    public async Task<CatalogRestoreResult> RestoreAsync(string backupDirectoryPath, ICatalogRestoreLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);

        var candidatePath = GetBackupDirectoryPath(backupDirectoryPath);
        var candidateValidation = await ValidateAsync(candidatePath);
        if (!candidateValidation.IsValid)
        {
            throw new InvalidDataException($"The selected backup cannot be restored: {candidateValidation.Error}");
        }

        var safetyBackup = await CreateCoreAsync("pre-restore");
        var lifecycleStopped = false;
        try
        {
            await lifecycle.StopAsync();
            lifecycleStopped = true;
            await RestoreFilesAsync(candidatePath);
            await lifecycle.ReinitializeAsync();
            await EnsureLiveDatabaseIsHealthyAsync();
            await lifecycle.RegenerateSnapshotAsync();
            var retentionWarning = await TryApplyRetentionAsync(candidatePath, safetyBackup.DirectoryPath);
            return new CatalogRestoreResult(true, candidatePath, safetyBackup.DirectoryPath, retentionWarning);
        }
        catch (Exception exception)
        {
            var rollbackSucceeded = false;
            Exception? rollbackException = null;
            try
            {
                if (!lifecycleStopped)
                {
                    await lifecycle.StopAsync();
                }

                await RestoreFilesAsync(safetyBackup.DirectoryPath);
                await lifecycle.ReinitializeAsync();
                await EnsureLiveDatabaseIsHealthyAsync();
                await lifecycle.RegenerateSnapshotAsync();
                rollbackSucceeded = true;
            }
            catch (Exception recoveryException)
            {
                rollbackException = recoveryException;
            }

            await TryApplyRetentionAsync(candidatePath, safetyBackup.DirectoryPath);
            var message = rollbackSucceeded
                ? "Restore failed after the current catalog was safely rolled back."
                : $"Restore failed and automatic rollback also failed: {rollbackException?.Message ?? "unknown rollback error"}";
            throw new CatalogRestoreException(message, rollbackSucceeded, exception);
        }
    }

    private async Task<CatalogBackupInfo> CreateCoreAsync(string reason)
    {
        if (!File.Exists(_options.DatabasePath))
        {
            throw new FileNotFoundException("Catalog database was not found.", _options.DatabasePath);
        }


        Directory.CreateDirectory(_options.BackupRootPath);
        var timestamp = DateTimeOffset.UtcNow;
        var backupName = $"catalog-{timestamp:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var stagingDirectoryPath = Path.Combine(_options.BackupRootPath, $".{backupName}.staging");
        var backupDirectoryPath = Path.Combine(_options.BackupRootPath, backupName);

        try
        {
            Directory.CreateDirectory(stagingDirectoryPath);
            var backupDatabasePath = Path.Combine(stagingDirectoryPath, DatabaseFileName);
            var backupSnapshotPath = Path.Combine(stagingDirectoryPath, SnapshotFileName);
            await CopyDatabaseOnlineAsync(_options.DatabasePath, backupDatabasePath);
            await RegenerateSnapshotFromDatabaseAsync(backupDatabasePath, backupSnapshotPath);

            var databaseSummary = await GetDatabaseSummaryAsync(backupDatabasePath);
            if (!string.Equals(databaseSummary.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SQLite integrity check failed: {databaseSummary.IntegrityResult}");
            }

            var snapshotCount = await GetSnapshotComponentCountAsync(backupSnapshotPath);
            if (snapshotCount != databaseSummary.ComponentCount)
            {
                throw new InvalidDataException("Catalog snapshot component count does not match the database.");
            }

            var metadata = new CatalogBackupMetadata(
                timestamp.ToString("O"),
                databaseSummary.ComponentCount,
                await GetSha256Async(backupDatabasePath),
                await GetSha256Async(backupSnapshotPath),
                databaseSummary.IntegrityResult,
                _options.AppVersion,
                _options.TaxonomyVersion,
                string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim());
            await WriteMetadataAtomicallyAsync(Path.Combine(stagingDirectoryPath, MetadataFileName), metadata);

            Directory.Move(stagingDirectoryPath, backupDirectoryPath);
            var validation = await ValidateAsync(backupDirectoryPath);
            if (!validation.IsValid)
            {
                throw new InvalidDataException($"Created backup did not validate: {validation.Error}");
            }

            return new CatalogBackupInfo(backupDirectoryPath, metadata, validation);
        }
        finally
        {
            if (Directory.Exists(stagingDirectoryPath))
            {
                Directory.Delete(stagingDirectoryPath, recursive: true);
            }
        }
    }

    private async Task RestoreFilesAsync(string backupDirectoryPath)
    {
        var directoryPath = GetBackupDirectoryPath(backupDirectoryPath);
        var backupDatabasePath = Path.Combine(directoryPath, DatabaseFileName);
        var backupSnapshotPath = Path.Combine(directoryPath, SnapshotFileName);
        await CopyFileAtomicallyAsync(backupDatabasePath, _options.DatabasePath);
        await CopyFileAtomicallyAsync(backupSnapshotPath, _options.SnapshotPath);
    }

    private async Task<string?> TryApplyRetentionAsync(params string[] protectedPaths)
    {
        try
        {
            await ApplyRetentionAsync(new HashSet<string>(protectedPaths, StringComparer.OrdinalIgnoreCase));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Backup retention cleanup failed: {exception.Message}";
        }
    }

    private async Task ApplyRetentionAsync(IReadOnlySet<string>? protectedPaths = null)
    {
        var protectedFullPaths = protectedPaths is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : protectedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var backups = await ListAsync();
        var removableBackups = backups
            .Where(backup => !protectedFullPaths.Contains(Path.GetFullPath(backup.DirectoryPath)))
            .Skip(Math.Max(0, _options.MaxBackups - protectedFullPaths.Count))
            .ToList();

        foreach (var backup in removableBackups)
        {
            Directory.Delete(backup.DirectoryPath, recursive: true);
        }
    }

    private async Task EnsureLiveDatabaseIsHealthyAsync()
    {
        var summary = await GetDatabaseSummaryAsync(_options.DatabasePath);
        if (!string.Equals(summary.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Restored SQLite database failed integrity check: {summary.IntegrityResult}");
        }
    }

    private static async Task RegenerateSnapshotFromDatabaseAsync(string databasePath, string snapshotPath)
    {
        await using var repository = new CatalogRepository(new CatalogRepositoryOptions(databasePath, snapshotPath));
        await repository.InitializeAsync();
        await repository.ExportSnapshotAsync();
    }

    private static async Task CopyDatabaseOnlineAsync(string sourcePath, string destinationPath)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await source.OpenAsync();
        await destination.OpenAsync();
        source.BackupDatabase(destination);
    }

    private static async Task CopyFileAtomicallyAsync(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteMetadataAtomicallyAsync(string metadataPath, CatalogBackupMetadata metadata)
    {
        var temporaryPath = $"{metadataPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var content = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<CatalogBackupMetadata> ReadMetadataAsync(string metadataPath)
    {
        await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<CatalogBackupMetadata>(stream, MetadataJsonOptions)
            ?? throw new InvalidDataException("Backup metadata is empty.");
    }

    private static void ValidateMetadata(CatalogBackupMetadata metadata)
    {
        if (!DateTimeOffset.TryParse(metadata.CreatedUtc, out _) ||
            metadata.ComponentCount < 0 ||
            !IsSha256(metadata.DatabaseSha256) ||
            !IsSha256(metadata.SnapshotSha256) ||
            !string.Equals(metadata.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(metadata.AppVersion) ||
            string.IsNullOrWhiteSpace(metadata.TaxonomyVersion))
        {
            throw new InvalidDataException("Backup metadata is invalid.");
        }
    }

    private static bool IsSha256(string hash) =>
        hash.Length == 64 && hash.All(Uri.IsHexDigit);

    private static async Task<bool> HashMatchesAsync(string path, string expectedHash)
    {
        var actualHash = await GetSha256Async(path);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualHash),
            Convert.FromHexString(expectedHash));
    }

    private static async Task<string> GetSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task<(string IntegrityResult, int ComponentCount)> GetDatabaseSummaryAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();

        await using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA integrity_check";
        var integrityResult = (string?)await integrityCommand.ExecuteScalarAsync() ?? "no result";

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM components";
        var count = (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
        return (integrityResult, checked((int)count));
    }

    private static async Task<int> GetSnapshotComponentCountAsync(string snapshotPath)
    {
        await using var stream = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("meta", out var metadata) ||
            !metadata.TryGetProperty("componentCount", out var componentCount) ||
            !componentCount.TryGetInt32(out var count) ||
            !root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Array ||
            components.GetArrayLength() != count)
        {
            throw new InvalidDataException("Catalog snapshot is not a consistent catalog export.");
        }

        return count;
    }

    private string GetBackupDirectoryPath(string backupDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectoryPath);
        var fullPath = Path.GetFullPath(backupDirectoryPath);
        var expectedParent = Path.TrimEndingDirectorySeparator(_options.BackupRootPath);
        var parent = Directory.GetParent(fullPath)?.FullName;
        if (!string.Equals(parent, expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith("catalog-", StringComparison.Ordinal))
        {
            throw new ArgumentException("Backup path must be a direct child of the configured backup directory.", nameof(backupDirectoryPath));
        }

        return fullPath;
    }

    private static CatalogBackupValidation Invalid(string error, CatalogBackupMetadata? metadata = null) =>
        new(false, error, metadata);

    private static CatalogBackupMetadata CreateUnavailableMetadata(string directoryPath) =>
        new(
            Directory.GetCreationTimeUtc(directoryPath).ToString("O"),
            0,
            string.Empty,
            string.Empty,
            "invalid",
            "unknown",
            "unknown",
            "unknown");
}
