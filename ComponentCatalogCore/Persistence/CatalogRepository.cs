using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Export;
using ComponentCatalogCore.Importing;
using ComponentCatalogCore.Search;
using Microsoft.Data.Sqlite;

namespace ComponentCatalogCore.Persistence;

public sealed class CatalogRepository : IAsyncDisposable
{
    private readonly CatalogRepositoryOptions _options;
    private readonly SqliteConnection _connection;

    public CatalogRepository(CatalogRepositoryOptions options)
    {
        _options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.ExportPath)!);
        _connection = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False");
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        await using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS components (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                part_number TEXT NOT NULL UNIQUE,
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
            """;
        await command.ExecuteNonQueryAsync();

        // 仅在已有数据时导出快照，避免初始化空库时生成空文件
        await using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM components";
        var count = (long)(await countCmd.ExecuteScalarAsync())!;
        if (count > 0)
        {
            await ExportSnapshotAsync();
        }
    }

    public async Task<IReadOnlyList<ComponentRecord>> ListComponentsAsync()
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

    public async Task<IReadOnlyList<string>> ListCategoriesAsync()
    {
        var categories = (await ListComponentsAsync())
            .Select(component => component.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCulture)
            .ToList();
        categories.Insert(0, "全部");
        return categories;
    }

    public async Task<ComponentRecord?> GetByIdAsync(string id)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM components WHERE id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    public async Task<ComponentRecord?> GetByPartNumberAsync(string partNumber)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM components WHERE part_number = $partNumber LIMIT 1";
        command.Parameters.AddWithValue("$partNumber", partNumber);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query)
    {
        var components = await ListComponentsAsync();
        return CatalogSearchService.Search(components, query);
    }

    public async Task UpsertComponentAsync(ComponentRecord component, bool exportSnapshot = true)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO components (
                id, name, part_number, category, package_name, aliases_json, keywords_json,
                attributes_json, owned, location, substitutes_json, notes, source_type
            ) VALUES (
                $id, $name, $partNumber, $category, $packageName, $aliases, $keywords,
                $attributes, $owned, $location, $substitutes, $notes, $sourceType
            )
            ON CONFLICT(part_number) DO UPDATE SET
                id = excluded.id,
                name = excluded.name,
                category = excluded.category,
                package_name = excluded.package_name,
                aliases_json = excluded.aliases_json,
                keywords_json = excluded.keywords_json,
                attributes_json = excluded.attributes_json,
                owned = excluded.owned,
                location = excluded.location,
                substitutes_json = excluded.substitutes_json,
                notes = excluded.notes,
                source_type = excluded.source_type;
            """;

        command.Parameters.AddWithValue("$id", component.Id);
        command.Parameters.AddWithValue("$name", component.Name);
        command.Parameters.AddWithValue("$partNumber", component.PartNumber);
        command.Parameters.AddWithValue("$category", component.Category);
        command.Parameters.AddWithValue("$packageName", component.Package);
        command.Parameters.AddWithValue("$aliases", JsonSerializer.Serialize(component.Aliases));
        command.Parameters.AddWithValue("$keywords", JsonSerializer.Serialize(component.Keywords));
        command.Parameters.AddWithValue("$attributes", JsonSerializer.Serialize(component.Attributes));
        command.Parameters.AddWithValue("$owned", component.Owned ? 1 : 0);
        command.Parameters.AddWithValue("$location", component.Location);
        command.Parameters.AddWithValue("$substitutes", JsonSerializer.Serialize(component.Substitutes));
        command.Parameters.AddWithValue("$notes", component.Notes);
        command.Parameters.AddWithValue("$sourceType", ComponentSourceTypeContract.ToContractValue(component.SourceType));

        await command.ExecuteNonQueryAsync();

        if (exportSnapshot)
        {
            await ExportSnapshotAsync();
        }
    }

    public async Task DeleteByIdAsync(string id)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM components WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
        await ExportSnapshotAsync();
    }

    public async Task<ImportParseResult> ImportCsvAsync(string input, DuplicateStrategy strategy)
    {
        return (await ImportCsvWithSummaryAsync(input, strategy)).ParseResult;
    }

    public async Task<CatalogImportSummary> ImportCsvWithSummaryAsync(string input, DuplicateStrategy strategy)
    {
        var parsed = CatalogImportService.ParseCsvRows(input);
        var insertedCount = 0;
        var skippedCount = 0;
        var overwrittenCount = 0;
        var mergedCount = 0;

        foreach (var row in parsed.ValidRows)
        {
            var existing = await GetByPartNumberAsync(row.PartNumber);
            if (existing is null)
            {
                await UpsertComponentAsync(ImportRowToComponent(row), exportSnapshot: false);
                insertedCount++;
                continue;
            }

            switch (strategy)
            {
                case DuplicateStrategy.Skip:
                    skippedCount++;
                    break;
                case DuplicateStrategy.Overwrite:
                    await UpsertComponentAsync(ImportRowToComponent(row, existing.Id, existing.Substitutes), exportSnapshot: false);
                    overwrittenCount++;
                    break;
                case DuplicateStrategy.MergeAlias:
                    await UpsertComponentAsync(new ComponentRecord
                    {
                        Id = existing.Id,
                        Name = existing.Name,
                        PartNumber = existing.PartNumber,
                        Category = existing.Category,
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
                    }, exportSnapshot: false);
                    mergedCount++;
                    break;
            }
        }

        await ExportSnapshotAsync();
        return new CatalogImportSummary
        {
            ParseResult = parsed,
            InsertedCount = insertedCount,
            SkippedCount = skippedCount,
            OverwrittenCount = overwrittenCount,
            MergedCount = mergedCount,
        };
    }

    public async Task ExportSnapshotAsync()
    {
        var snapshot = CatalogSnapshotService.BuildSnapshot(await ListComponentsAsync(), DateTimeOffset.UtcNow.ToString("O"));
        var json = JsonSerializer.Serialize(snapshot, CatalogJson.DefaultOptions);
        await File.WriteAllTextAsync(_options.ExportPath, json);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private static ComponentRecord ImportRowToComponent(ImportRow row, string? id = null, IReadOnlyList<string>? substitutes = null)
    {
        return new ComponentRecord
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = row.Name,
            PartNumber = row.PartNumber,
            Category = row.Category,
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
            Category = reader.GetString(reader.GetOrdinal("category")),
            Package = reader.GetString(reader.GetOrdinal("package_name")),
            Aliases = DeserializeList(reader.GetString(reader.GetOrdinal("aliases_json"))),
            Keywords = DeserializeList(reader.GetString(reader.GetOrdinal("keywords_json"))),
            Attributes = DeserializeDictionary(reader.GetString(reader.GetOrdinal("attributes_json"))),
            Owned = reader.GetInt32(reader.GetOrdinal("owned")) == 1,
            Location = reader.GetString(reader.GetOrdinal("location")),
            Substitutes = DeserializeList(reader.GetString(reader.GetOrdinal("substitutes_json"))),
            Notes = reader.GetString(reader.GetOrdinal("notes")),
            SourceType = ComponentSourceTypeContract.Parse(reader.GetString(reader.GetOrdinal("source_type"))),
        };
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }
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
