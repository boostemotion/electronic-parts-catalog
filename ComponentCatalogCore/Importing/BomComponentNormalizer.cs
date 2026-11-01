using System.Text.RegularExpressions;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Taxonomy;

namespace ComponentCatalogCore.Importing;

public static partial class BomComponentNormalizer
{
    public const int MaximumKeywords = 5;
    public const int MaximumAttributes = 5;
    public const int MaximumNotesLength = 240;
    public const int MaximumDesignators = 100;

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
    {
        "电容", "电阻", "电感", "电源", "二极管", "三极管", "连接器", "集成电路", "晶振", "其他",
    };

    public static BomNormalizationResult Normalize(BomImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var warnings = new List<string>();
        var category = DetermineCategory(row, warnings);
        var hasManufacturerPart = !string.IsNullOrWhiteSpace(row.ManufacturerPart);
        var normalizedValue = NormalizePassiveValue(row.Value);
        var partNumber = hasManufacturerPart ? row.ManufacturerPart : NormalizePassiveValue(row.Value);
        var displayName = string.IsNullOrWhiteSpace(normalizedValue) ? partNumber : normalizedValue;
        var package = NormalizePackage(row.Footprint);
        var substitutes = NormalizeSubstitutes(row, warnings);
        var notes = NormalizeNotes(row.Notes, warnings);

        return new BomNormalizationResult
        {
            Record = new ComponentRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = displayName,
                PartNumber = partNumber,
                Category = category.SubCategory,
                MajorCategoryId = category.MajorCategoryId,
                SubCategoryId = category.SubCategoryId,
                Package = package,
                Aliases = [],
                Keywords = NormalizeKeywords(row.Keywords, partNumber, package, category.SubCategory),
                Attributes = CreateAttributes(row, hasManufacturerPart),
                Owned = true,
                Location = string.Empty,
                Substitutes = substitutes,
                Notes = notes,
                Quantity = Math.Max(row.Quantity, 0),
                Designators = NormalizeDesignators(row.Designators),
                SourceType = ComponentSourceType.CsvImport,
            },
            Warnings = warnings,
        };
    }

    private static ComponentCategoryAssignment DetermineCategory(BomImportRow row, ICollection<string> warnings)
    {
        var partNumber = row.ManufacturerPart.Trim();
        if (partNumber.StartsWith("STM32", StringComparison.OrdinalIgnoreCase)) return ComponentCategoryTaxonomy.GetAssignment("mcu-logic", "mcu-mpu-soc");
        if (partNumber.StartsWith("AMS1117", StringComparison.OrdinalIgnoreCase)) return ComponentCategoryTaxonomy.GetAssignment("protection-power", "ldo");
        if (partNumber.StartsWith("TPS54302", StringComparison.OrdinalIgnoreCase)) return ComponentCategoryTaxonomy.GetAssignment("protection-power", "dc-dc");
        if (partNumber.StartsWith("CH224K", StringComparison.OrdinalIgnoreCase)) return ComponentCategoryTaxonomy.GetAssignment("protection-power", "quick-charge-controller");

        var designator = row.Designators.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        if (designator?.StartsWith("C", StringComparison.OrdinalIgnoreCase) == true) return ComponentCategoryTaxonomy.GetAssignment("passives", "mlcc");
        if (designator?.StartsWith("R", StringComparison.OrdinalIgnoreCase) == true) return ComponentCategoryTaxonomy.GetAssignment("passives", "chip-resistor");
        if (designator?.StartsWith("L", StringComparison.OrdinalIgnoreCase) == true) return ComponentCategoryTaxonomy.GetAssignment("passives", "chip-inductor");

        var supplied = row.Category.Trim();
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            var resolved = ComponentCategoryTaxonomy.ResolveLegacy(supplied, row.Footprint, partNumber);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        warnings.Add(string.IsNullOrWhiteSpace(supplied)
            ? "无法从位号、型号或分类确定元件分类，已标记为未分类且需要人工复核。"
            : $"分类“{supplied}”不在固定分类体系中，已标记为未分类且需要人工复核。");
        return ComponentCategoryTaxonomy.GetUncategorizedAssignment();
    }

    private static IReadOnlyList<string> NormalizeKeywords(
        IEnumerable<string> keywords,
        string partNumber,
        string package,
        string category)
    {
        var excluded = new HashSet<string>([partNumber, package, category], StringComparer.OrdinalIgnoreCase);
        return keywords
            .Select(keyword => keyword.Trim())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword) && !excluded.Contains(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumKeywords)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> CreateAttributes(BomImportRow row, bool hasManufacturerPart)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddAttribute(attributes, "footprint", row.Footprint);
        if (hasManufacturerPart)
        {
            AddAttribute(attributes, "value", NormalizePassiveValue(row.Value));
        }

        foreach (var attribute in row.Attributes)
        {
            if (attributes.Count == MaximumAttributes)
            {
                break;
            }

            AddAttribute(attributes, attribute.Key, attribute.Value);
        }

        return attributes;
    }

    private static void AddAttribute(IDictionary<string, string> attributes, string key, string value)
    {
        var normalizedKey = key.Trim();
        var normalizedValue = value.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey) && !string.IsNullOrWhiteSpace(normalizedValue))
        {
            attributes.TryAdd(normalizedKey, normalizedValue);
        }
    }

    private static IReadOnlyList<string> NormalizeSubstitutes(BomImportRow row, ICollection<string> warnings)
    {
        if (row.Substitutes.Count == 0)
        {
            return [];
        }

        if (!row.AreSubstitutesVerified)
        {
            warnings.Add("存在未验证的替代料，未写入目录。 ");
            return [];
        }

        return row.Substitutes
            .Select(substitute => substitute.Trim())
            .Where(substitute => !string.IsNullOrWhiteSpace(substitute))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumKeywords)
            .ToList();
    }

    private static IReadOnlyList<string> NormalizeDesignators(IEnumerable<string> designators) =>
        designators
            .Select(designator => designator.Trim())
            .Where(designator => !string.IsNullOrWhiteSpace(designator))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDesignators)
            .ToList();

    private static string NormalizeNotes(string notes, ICollection<string> warnings)
    {
        var normalized = notes.Trim();
        if (normalized.Length <= MaximumNotesLength)
        {
            return normalized;
        }

        warnings.Add($"备注已截断为 {MaximumNotesLength} 个字符。");
        return normalized[..MaximumNotesLength];
    }

    private static string NormalizePassiveValue(string value)
    {
        var normalized = value.Trim().Replace('μ', 'u').Replace('µ', 'u');
        var match = PassiveValuePattern().Match(normalized);
        if (!match.Success)
        {
            return normalized;
        }

        var number = match.Groups["number"].Value;
        return match.Groups["suffix"].Value.ToUpperInvariant() switch
        {
            "NF" => $"{number}nF",
            "PF" => $"{number}pF",
            "UF" => $"{number}uF",
            "UH" => $"{number}uH",
            "MH" => $"{number}mH",
            "K" => $"{number}k",
            "M" => $"{number}M",
            "R" => $"{number}R",
            _ => normalized,
        };
    }

    private static string NormalizePackage(string footprint)
    {
        var match = PackagePattern().Match(footprint.Trim());
        return match.Success ? match.Groups["package"].Value : footprint.Trim();
    }

    [GeneratedRegex("^(?<number>\\d+(?:\\.\\d+)?)(?<suffix>[A-Za-z]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PassiveValuePattern();

    [GeneratedRegex("(?<!\\d)(?<package>\\d{4})(?!\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();
}
