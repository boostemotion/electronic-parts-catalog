using System.Globalization;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Utilities;

namespace ComponentCatalogWinUI.Models;

public sealed class ComponentItem
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PartNumber { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Package { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Attributes { get; set; } = string.Empty;

    public IReadOnlyList<string> Aliases { get; set; } = [];

    public IReadOnlyList<string> Keywords { get; set; } = [];

    public IReadOnlyList<string> Substitutes { get; set; } = [];

    public string Notes { get; set; } = string.Empty;

    public bool Owned { get; set; }

    public ComponentSourceType SourceType { get; set; }

    public string OwnedBadgeText => Owned ? "持有" : "未持有";

    public string CategoryGlyph => Category switch
    {
        "MCU" => "\uE899",
        "电源" => "\uE945",
        "电阻" => "\uEADF",
        "连接器" => "\uE8CD",
        _ => "\uE8B7",
    };

    public static ComponentItem FromRecord(ComponentRecord record)
    {
        return new ComponentItem
        {
            Id = record.Id,
            Name = record.Name,
            PartNumber = record.PartNumber,
            Category = record.Category,
            Package = record.Package,
            Location = record.Location,
            Attributes = FormatAttributes(record.Attributes),
            Aliases = record.Aliases.ToList(),
            Keywords = record.Keywords.ToList(),
            Substitutes = record.Substitutes.ToList(),
            Notes = record.Notes,
            Owned = record.Owned,
            SourceType = record.SourceType,
        };
    }

    private static string FormatAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        return string.Join("; ", attributes.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));
    }

    public static string FormatList(IReadOnlyList<string> values)
    {
        return CatalogTextCodec.FormatList(values);
    }
}
