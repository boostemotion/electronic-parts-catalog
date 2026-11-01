using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Storage;

public sealed record CatalogStorageMigrationResult(bool Attempted, bool Succeeded, bool DestinationAlreadyExists, string? Error)
{
    public static CatalogStorageMigrationResult NotNeeded() => new(false, true, false, null);
    public static CatalogStorageMigrationResult AlreadyExists() => new(false, true, true, null);
    public static CatalogStorageMigrationResult Success() => new(true, true, false, null);
    public static CatalogStorageMigrationResult Failed(Exception exception) => new(true, false, false, exception.Message);
}

public static class CatalogStorageMigration
{
    public static async Task<CatalogStorageMigrationResult> MigrateDatabaseAsync(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return CatalogStorageMigrationResult.AlreadyExists();
        }

        if (!File.Exists(sourcePath))
        {
            return CatalogStorageMigrationResult.NotNeeded();
        }

        var temporaryPath = CreateTemporaryPath(destinationPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False"))
            await using (var destination = new SqliteConnection($"Data Source={temporaryPath};Pooling=False"))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);

                await using var integrityCheck = destination.CreateCommand();
                integrityCheck.CommandText = "PRAGMA integrity_check";
                var result = (string?)await integrityCheck.ExecuteScalarAsync();
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"SQLite integrity check failed: {result ?? "no result"}.");
                }
            }

            File.Move(temporaryPath, destinationPath);
            return CatalogStorageMigrationResult.Success();
        }
        catch (Exception exception)
        {
            return CatalogStorageMigrationResult.Failed(exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<CatalogStorageMigrationResult> MigrateSnapshotAsync(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return CatalogStorageMigrationResult.AlreadyExists();
        }

        if (!File.Exists(sourcePath))
        {
            return CatalogStorageMigrationResult.NotNeeded();
        }

        var temporaryPath = CreateTemporaryPath(destinationPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }

            await using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using var document = await JsonDocument.ParseAsync(stream);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Snapshot must be a JSON object.");
                }
            }

            File.Move(temporaryPath, destinationPath);
            return CatalogStorageMigrationResult.Success();
        }
        catch (Exception exception)
        {
            return CatalogStorageMigrationResult.Failed(exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateTemporaryPath(string destinationPath) =>
        $"{destinationPath}.{Guid.NewGuid():N}.migration.tmp";
}
