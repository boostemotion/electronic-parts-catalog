using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComponentCatalogCore.Contracts;

[JsonConverter(typeof(ComponentSourceTypeJsonConverter))]
public enum ComponentSourceType
{
    Manual,
    CsvImport,
}

public sealed class ComponentRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    // Temporary compatibility display value. Validation derives it from SubCategoryId.
    public string Category { get; set; } = string.Empty;
    public string MajorCategoryId { get; set; } = string.Empty;
    public string SubCategoryId { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public IReadOnlyList<string> Aliases { get; set; } = [];
    public IReadOnlyList<string> Keywords { get; set; } = [];
    public IReadOnlyDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    public bool Owned { get; set; }
    public string Location { get; set; } = string.Empty;
    public IReadOnlyList<string> Substitutes { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
    public ComponentSourceType SourceType { get; set; }
}

public sealed class SearchQuery
{
    public string Text { get; set; } = string.Empty;
    public string Category { get; set; } = "全部";
    public string? MajorCategoryId { get; set; }
    public string? SubCategoryId { get; set; }
    public string? Package { get; set; }
    public bool? Owned { get; set; }
    public string? AttributeText { get; set; }
}

public sealed class SearchResult
{
    public required ComponentRecord Component { get; init; }
    public required int Score { get; init; }
    public required IReadOnlyList<string> MatchedFields { get; init; }
}

public sealed class ImportRow
{
    public string Name { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    // Temporary compatibility display value. Validation derives it from SubCategoryId.
    public string Category { get; set; } = string.Empty;
    public string MajorCategoryId { get; set; } = string.Empty;
    public string SubCategoryId { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public bool Owned { get; set; }
    public IReadOnlyDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Aliases { get; set; } = [];
    public IReadOnlyList<string> Keywords { get; set; } = [];
    public string Location { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public ComponentSourceType SourceType { get; set; }
}

public sealed class ImportError
{
    public required int Line { get; init; }
    public required string Reason { get; init; }
}

public sealed class ImportParseResult
{
    public required IReadOnlyList<ImportRow> ValidRows { get; init; }
    public required IReadOnlyList<ImportError> Errors { get; init; }
}

public sealed class ExportCatalogSnapshot
{
    public required ExportCatalogMeta Meta { get; init; }
    public required IReadOnlyList<ComponentRecord> Components { get; init; }
}

public sealed class ExportCatalogMeta
{
    public required string ExportedAt { get; init; }
    public required int ComponentCount { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public required string TaxonomyVersion { get; init; }
    public required IReadOnlyList<ComponentCatalogCore.Taxonomy.ComponentCategoryDefinition> MajorCategories { get; init; }
}

public static class ComponentSourceTypeContract
{
    public static string ToContractValue(ComponentSourceType sourceType)
    {
        return sourceType switch
        {
            ComponentSourceType.Manual => "manual",
            ComponentSourceType.CsvImport => "csv-import",
            _ => "manual",
        };
    }

    public static ComponentSourceType Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "manual" => ComponentSourceType.Manual,
            "csv-import" => ComponentSourceType.CsvImport,
            "csvimport" => ComponentSourceType.CsvImport,
            _ => ComponentSourceType.Manual,
        };
    }
}

public sealed class ComponentSourceTypeJsonConverter : JsonConverter<ComponentSourceType>
{
    public override ComponentSourceType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ComponentSourceTypeContract.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, ComponentSourceType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ComponentSourceTypeContract.ToContractValue(value));
    }
}
