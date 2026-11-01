using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Taxonomy;
using ComponentCatalogCore.Validation;
using ComponentCatalogCore.Export;
using ComponentCatalogCore.Importing;
using ComponentCatalogCore.Search;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Persistence;

public sealed class CatalogRepository : IAsyncDisposable
{
    private readonly CatalogRepositoryOptions _options;
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public CatalogRepository(CatalogRepositoryOptions options)
    {
        _options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.ExportPath)!);
        _connection = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False");
    }

    public async Task InitializeAsync()
    {
        await EnterOperationAsync();
        try
        {
            await InitializeCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<ComponentRecord>> ListComponentsAsync()
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            return await ListComponentsCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync()
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            return ["全部", .. ComponentCategoryTaxonomy.MajorCategories.Select(category => category.Label)];
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ComponentRecord?> GetByIdAsync(string id)
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            return await GetByIdCoreAsync(id);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ComponentRecord?> GetByPartNumberAsync(string partNumber)
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            return await GetByPartNumberCoreAsync(partNumber);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ComponentRecord?> GetByPartNumberAndPackageAsync(string partNumber, string package)
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            return await GetByPartNumberAndPackageCoreAsync(partNumber, package);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query)
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            return CatalogSearchService.Search(await ListComponentsCoreAsync(), query);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SaveComponentAsync(ComponentRecord component, bool exportSnapshot = true)
    {
        ValidateComponent(component);
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            await SaveComponentCoreAsync(component);
            if (exportSnapshot)
            {
                await ExportSnapshotCoreAsync();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SaveComponentsAsync(IReadOnlyList<ComponentRecord> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
            try
            {
                foreach (var component in components)
                {
                    ValidateComponent(component);
                    await SaveComponentCoreAsync(component, transaction);
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await ExportSnapshotCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task UpsertComponentAsync(ComponentRecord component, bool exportSnapshot = true) =>
        SaveComponentAsync(component, exportSnapshot);

    public async Task DeleteByIdAsync(string id)
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM components WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
            await ExportSnapshotCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ImportParseResult> ImportCsvAsync(string input, DuplicateStrategy strategy)
    {
        return (await ImportCsvWithSummaryAsync(input, strategy)).ParseResult;
    }

    public async Task<CatalogImportSummary> ImportCsvWithSummaryAsync(string input, DuplicateStrategy strategy)
    {
        var parsed = CatalogImportService.ParseCsvRows(input);
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            var insertedCount = 0;
            var skippedCount = 0;
            var overwrittenCount = 0;
            var mergedCount = 0;

            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
            try
            {
                foreach (var row in parsed.ValidRows)
                {
                    var existing = await GetByPartNumberAndPackageCoreAsync(row.PartNumber, row.Package, transaction);
                    if (existing is null)
                    {
                        await SaveComponentCoreAsync(ImportRowToComponent(row), transaction);
                        insertedCount++;
                        continue;
                    }

                    switch (strategy)
                    {
                        case DuplicateStrategy.Skip:
                            skippedCount++;
                            break;
                        case DuplicateStrategy.Overwrite:
                            await SaveComponentCoreAsync(ImportRowToComponent(row, existing.Id, existing.Substitutes), transaction);
                            overwrittenCount++;
                            break;
                        case DuplicateStrategy.MergeAlias:
                            await SaveComponentCoreAsync(new ComponentRecord
                            {
                                Id = existing.Id,
                                Name = existing.Name,
                                PartNumber = existing.PartNumber,
                                Category = existing.Category,
                                MajorCategoryId = existing.MajorCategoryId,
                                SubCategoryId = existing.SubCategoryId,
                                Package = existing.Package,
                                Aliases = existing.Aliases.Concat(row.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                                Keywords = existing.Keywords.Concat(row.Keywords).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                                Attributes = existing.Attributes.Concat(row.Attributes)
                                    .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase),
                                Owned = existing.Owned,
                                Location = existing.Location,
                                Substitutes = existing.Substitutes,
                                Notes = existing.Notes,
                                SourceType = existing.SourceType,
                            }, transaction);
                            mergedCount++;
                            break;
                    }
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            if (insertedCount + overwrittenCount + mergedCount > 0)
            {
                await ExportSnapshotCoreAsync();
            }

            return new CatalogImportSummary
            {
                ParseResult = parsed,
                InsertedCount = insertedCount,
                SkippedCount = skippedCount,
                OverwrittenCount = overwrittenCount,
                MergedCount = mergedCount,
            };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ExportSnapshotAsync()
    {
        await EnterOperationAsync();
        try
        {
            EnsureInitialized();
            await ExportSnapshotCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _connection.DisposeAsync();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private async Task EnterOperationAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
    }

    private async Task InitializeCoreAsync()
    {
        if (_initialized)
        {
            return;
        }

        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }

        await CreateComponentsTableAsync();
        await MigrateLegacyPartNumberUniquenessAsync();
        await EnsureOptionalColumnsAsync();
        await EnsureComponentIdentityIndexAsync();
        _initialized = true;

        await using var countCommand = _connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM components";
        var count = (long)(await countCommand.ExecuteScalarAsync())!;
        if (count > 0)
        {
            await ExportSnapshotCoreAsync();
        }
    }

    private async Task CreateComponentsTableAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS components (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                part_number TEXT NOT NULL,
                category TEXT NOT NULL,
                major_category_id TEXT NOT NULL DEFAULT 'other',
                sub_category_id TEXT NOT NULL DEFAULT 'uncategorized',
                package_name TEXT NOT NULL,
                aliases_json TEXT NOT NULL,
                keywords_json TEXT NOT NULL,
                attributes_json TEXT NOT NULL,
                owned INTEGER NOT NULL,
                location TEXT NOT NULL,
                substitutes_json TEXT NOT NULL,
                notes TEXT NOT NULL,
                quantity INTEGER NOT NULL DEFAULT 0,
                designators_json TEXT NOT NULL DEFAULT '[]',
                source_type TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureOptionalColumnsAsync()
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM pragma_table_info('components')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingColumns.Add(reader.GetString(0));
            }
        }

        if (!existingColumns.Contains("major_category_id"))
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "ALTER TABLE components ADD COLUMN major_category_id TEXT NOT NULL DEFAULT 'other'";
            await command.ExecuteNonQueryAsync();
        }

        if (!existingColumns.Contains("sub_category_id"))
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "ALTER TABLE components ADD COLUMN sub_category_id TEXT NOT NULL DEFAULT 'uncategorized'";
            await command.ExecuteNonQueryAsync();
        }

        if (!existingColumns.Contains("quantity"))
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "ALTER TABLE components ADD COLUMN quantity INTEGER NOT NULL DEFAULT 0";
            await command.ExecuteNonQueryAsync();
        }

        if (!existingColumns.Contains("designators_json"))
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "ALTER TABLE components ADD COLUMN designators_json TEXT NOT NULL DEFAULT '[]'";
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task MigrateLegacyPartNumberUniquenessAsync()
    {
        if (!await HasLegacyPartNumberUniqueIndexAsync())
        {
            return;
        }

        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
        try
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                CREATE TABLE components_identity_migration (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    part_number TEXT NOT NULL,
                    category TEXT NOT NULL,
                    package_name TEXT NOT NULL,
                    aliases_json TEXT NOT NULL,
                    keywords_json TEXT NOT NULL,
                    attributes_json TEXT NOT NULL,
                    owned INTEGER NOT NULL,
                    location TEXT NOT NULL,
                    substitutes_json TEXT NOT NULL,
                    notes TEXT NOT NULL,
                    source_type TEXT NOT NULL
                );
                INSERT INTO components_identity_migration (
                    id, name, part_number, category, package_name, aliases_json, keywords_json,
                    attributes_json, owned, location, substitutes_json, notes, source_type
                )
                SELECT
                    id, name, part_number, category, package_name, aliases_json, keywords_json,
                    attributes_json, owned, location, substitutes_json, notes, source_type
                FROM components;
                DROP TABLE components;
                ALTER TABLE components_identity_migration RENAME TO components;
                """;
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<bool> HasLegacyPartNumberUniqueIndexAsync()
    {
        var indexNames = new List<string>();
        await using (var listIndexes = _connection.CreateCommand())
        {
            listIndexes.CommandText = "SELECT name FROM pragma_index_list('components') WHERE \"unique\" = 1";
            await using var reader = await listIndexes.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexNames.Add(reader.GetString(0));
            }
        }

        foreach (var indexName in indexNames)
        {
            await using var indexColumns = _connection.CreateCommand();
            indexColumns.CommandText = "SELECT name FROM pragma_index_info($indexName) ORDER BY seqno";
            indexColumns.Parameters.AddWithValue("$indexName", indexName);
            await using var reader = await indexColumns.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            if (columns.Count == 1 && string.Equals(columns[0], "part_number", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task EnsureComponentIdentityIndexAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_components_part_number_package ON components(part_number, package_name)";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<ComponentRecord>> ListComponentsCoreAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM components ORDER BY category COLLATE NOCASE, name COLLATE NOCASE";

        await using var reader = await command.ExecuteReaderAsync();
        var components = new List<ComponentRecord>();
        while (await reader.ReadAsync())
        {
            components.Add(MapRow(reader));
        }

        return components;
    }

    private async Task<ComponentRecord?> GetByIdCoreAsync(string id, SqliteTransaction? transaction = null)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM components WHERE id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    private async Task<ComponentRecord?> GetByPartNumberCoreAsync(string partNumber, SqliteTransaction? transaction = null)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM components WHERE part_number = $partNumber ORDER BY id LIMIT 1";
        command.Parameters.AddWithValue("$partNumber", partNumber);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    private async Task<ComponentRecord?> GetByPartNumberAndPackageCoreAsync(string partNumber, string package, SqliteTransaction? transaction = null)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM components WHERE part_number = $partNumber AND package_name = $package LIMIT 1";
        command.Parameters.AddWithValue("$partNumber", partNumber);
        command.Parameters.AddWithValue("$package", package);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    private async Task SaveComponentCoreAsync(ComponentRecord component, SqliteTransaction? transaction = null)
    {
        var exists = await GetByIdCoreAsync(component.Id, transaction) is not null;
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = exists
            ? """
              UPDATE components SET
                  name = $name,
                  part_number = $partNumber,
                  category = $category,
                  major_category_id = $majorCategoryId,
                  sub_category_id = $subCategoryId,
                  package_name = $packageName,
                  aliases_json = $aliases,
                  keywords_json = $keywords,
                  attributes_json = $attributes,
                  owned = $owned,
                  location = $location,
                  substitutes_json = $substitutes,
                  notes = $notes,
                  quantity = $quantity,
                  designators_json = $designators,
                  source_type = $sourceType
              WHERE id = $id;
              """
            : """
              INSERT INTO components (
                  id, name, part_number, category, major_category_id, sub_category_id, package_name, aliases_json, keywords_json,
                  attributes_json, owned, location, substitutes_json, notes, quantity, designators_json, source_type
              ) VALUES (
                  $id, $name, $partNumber, $category, $majorCategoryId, $subCategoryId, $packageName, $aliases, $keywords,
                  $attributes, $owned, $location, $substitutes, $notes, $quantity, $designators, $sourceType
              );
              """;
        AddComponentParameters(command, component);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException exception) when (IsComponentIdentityConflict(exception))
        {
            throw new DuplicatePartNumberException(component.PartNumber, component.Package, exception);
        }
    }

    private async Task ExportSnapshotCoreAsync()
    {
        var snapshot = CatalogSnapshotService.BuildSnapshot(await ListComponentsCoreAsync(), DateTimeOffset.UtcNow.ToString("O"));
        var json = JsonSerializer.Serialize(snapshot, CatalogJson.DefaultOptions);
        var directory = Path.GetDirectoryName(_options.ExportPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(_options.ExportPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json);
                await writer.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _options.ExportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void AddComponentParameters(SqliteCommand command, ComponentRecord component)
    {
        command.Parameters.AddWithValue("$id", component.Id);
        command.Parameters.AddWithValue("$name", component.Name);
        command.Parameters.AddWithValue("$partNumber", component.PartNumber);
        command.Parameters.AddWithValue("$category", component.Category);
        command.Parameters.AddWithValue("$majorCategoryId", component.MajorCategoryId);
        command.Parameters.AddWithValue("$subCategoryId", component.SubCategoryId);
        command.Parameters.AddWithValue("$packageName", component.Package);
        command.Parameters.AddWithValue("$aliases", JsonSerializer.Serialize(component.Aliases));
        command.Parameters.AddWithValue("$keywords", JsonSerializer.Serialize(component.Keywords));
        command.Parameters.AddWithValue("$attributes", JsonSerializer.Serialize(component.Attributes));
        command.Parameters.AddWithValue("$owned", component.Owned ? 1 : 0);
        command.Parameters.AddWithValue("$location", component.Location);
        command.Parameters.AddWithValue("$substitutes", JsonSerializer.Serialize(component.Substitutes));
        command.Parameters.AddWithValue("$notes", component.Notes);
        command.Parameters.AddWithValue("$quantity", component.Quantity);
        command.Parameters.AddWithValue("$designators", JsonSerializer.Serialize(component.Designators));
        command.Parameters.AddWithValue("$sourceType", ComponentSourceTypeContract.ToContractValue(component.SourceType));
    }

    private static bool IsComponentIdentityConflict(SqliteException exception) =>
        exception.SqliteErrorCode == 19 &&
        (exception.Message.Contains("components.part_number", StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains("ux_components_part_number_package", StringComparison.OrdinalIgnoreCase));

    private static void ValidateComponent(ComponentRecord component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var validationError = ComponentRecordValidator.ValidateAndNormalize(component);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError, nameof(component));
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The catalog repository must be initialized before use.");
        }
    }

    private static ComponentRecord ImportRowToComponent(ImportRow row, string? id = null, IReadOnlyList<string>? substitutes = null)
    {
        return new ComponentRecord
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = row.Name,
            PartNumber = row.PartNumber,
            Category = row.Category,
            MajorCategoryId = row.MajorCategoryId,
            SubCategoryId = row.SubCategoryId,
            Package = row.Package,
            Aliases = row.Aliases,
            Keywords = row.Keywords,
            Attributes = row.Attributes,
            Owned = row.Owned,
            Location = row.Location,
            Substitutes = substitutes ?? [],
            Notes = row.Notes,
            SourceType = row.SourceType,
        };
    }

    private static ComponentRecord MapRow(SqliteDataReader reader)
    {
        return new ComponentRecord
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            PartNumber = reader.GetString(reader.GetOrdinal("part_number")),
            Category = ComponentCategoryTaxonomy.GetAssignment(
                reader.GetString(reader.GetOrdinal("major_category_id")),
                reader.GetString(reader.GetOrdinal("sub_category_id"))).SubCategory,
            MajorCategoryId = reader.GetString(reader.GetOrdinal("major_category_id")),
            SubCategoryId = reader.GetString(reader.GetOrdinal("sub_category_id")),
            Package = reader.GetString(reader.GetOrdinal("package_name")),
            Aliases = DeserializeList(reader.GetString(reader.GetOrdinal("aliases_json"))),
            Keywords = DeserializeList(reader.GetString(reader.GetOrdinal("keywords_json"))),
            Attributes = DeserializeDictionary(reader.GetString(reader.GetOrdinal("attributes_json"))),
            Owned = reader.GetInt32(reader.GetOrdinal("owned")) == 1,
            Location = reader.GetString(reader.GetOrdinal("location")),
            Substitutes = DeserializeList(reader.GetString(reader.GetOrdinal("substitutes_json"))),
            Notes = reader.GetString(reader.GetOrdinal("notes")),
            Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
            Designators = DeserializeList(reader.GetString(reader.GetOrdinal("designators_json"))),
            SourceType = ComponentSourceTypeContract.Parse(reader.GetString(reader.GetOrdinal("source_type"))),
        };
    }

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}

public sealed class DuplicatePartNumberException : InvalidOperationException
{
    public DuplicatePartNumberException(string partNumber, Exception? innerException = null)
        : this(partNumber, string.Empty, innerException)
    {
    }

    public DuplicatePartNumberException(string partNumber, string package, Exception? innerException = null)
        : base(string.IsNullOrWhiteSpace(package)
            ? $"Part number '{partNumber}' is already used by another component."
            : $"Part number '{partNumber}' with package '{package}' is already used by another component.", innerException)
    {
        PartNumber = partNumber;
        Package = package;
    }

    public string PartNumber { get; }
    public string Package { get; }
}

public enum DuplicateStrategy
{
    Overwrite,
    Skip,
    MergeAlias,
}

public sealed class CatalogImportSummary
{
    public required ImportParseResult ParseResult { get; init; }
    public required int InsertedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required int OverwrittenCount { get; init; }
    public required int MergedCount { get; init; }
    public int ValidRowCount => ParseResult.ValidRows.Count;
    public int ErrorCount => ParseResult.Errors.Count;
    public int ChangedCount => InsertedCount + OverwrittenCount + MergedCount;
}
