using ComponentCatalogCore.Contracts;

namespace ComponentCatalogCore.Importing;

public sealed class BomImportRow
{
    public int Quantity { get; set; }
    public IReadOnlyList<string> Designators { get; set; } = [];
    public string Value { get; set; } = string.Empty;
    public string Footprint { get; set; } = string.Empty;
    public string ManufacturerPart { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string SupplierPart { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public IReadOnlyList<string> Keywords { get; set; } = [];
    public IReadOnlyDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Substitutes { get; set; } = [];
    public bool AreSubstitutesVerified { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class BomImportPreviewRequest
{
    public IReadOnlyList<BomImportRow> Rows { get; set; } = [];
}

public sealed class BomImportCommitRequest
{
    public string PreviewId { get; set; } = string.Empty;
    public IReadOnlyList<int> RowIndexes { get; set; } = [];
}

public sealed class BomImportPreview
{
    public required string PreviewId { get; init; }
    public required IReadOnlyList<BomImportPreviewItem> Items { get; init; }
}

public sealed class BomImportPreviewItem
{
    public required int RowIndex { get; init; }
    public required BomMatchAction Action { get; init; }
    public required double Confidence { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required ComponentRecord Record { get; init; }
    public string? MatchedComponentId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ComponentRecord CommitRecord { get; init; } = new();
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BomMatchAction>))]
public enum BomMatchAction
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("create")]
    Create,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("merge")]
    Merge,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("review")]
    Review,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("skip")]
    Skip,
}

public sealed class BomMatchResult
{
    public required BomMatchAction Action { get; init; }
    public required double Confidence { get; init; }
    public ComponentRecord? MatchedComponent { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class BomNormalizationResult
{
    public required ComponentRecord Record { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
