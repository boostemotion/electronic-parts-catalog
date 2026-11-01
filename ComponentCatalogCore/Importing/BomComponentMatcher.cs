using System.Text.RegularExpressions;
using ComponentCatalogCore.Contracts;

namespace ComponentCatalogCore.Importing;

public static partial class BomComponentMatcher
{
    public static BomMatchResult Match(
        ComponentRecord proposed,
        bool hasManufacturerPart,
        IEnumerable<ComponentRecord> existingRecords)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(existingRecords);
        var existing = existingRecords.ToList();
        var exactModel = existing.Where(component => SameModel(proposed.PartNumber, component.PartNumber)).ToList();
        var exactModelAndPackage = exactModel
            .Where(component => SamePackage(proposed.Package, component.Package))
            .ToList();

        if (hasManufacturerPart)
        {
            if (exactModelAndPackage.Count == 1)
            {
                return Merge(exactModelAndPackage[0], 1d);
            }

            if (exactModel.Count > 0 || existing.Any(component => IsSameManufacturerFamily(proposed.PartNumber, component.PartNumber)))
            {
                return Review(exactModel.FirstOrDefault(), "型号相近或封装不同，需要人工确认。");
            }

            return Create();
        }

        var passiveMatches = exactModelAndPackage
            .Where(component => SameCategory(proposed.Category, component.Category))
            .ToList();
        if (passiveMatches.Count == 1)
        {
            return Merge(passiveMatches[0], IsLegacyConcatenated(passiveMatches[0].PartNumber) ? 0.9d : 1d);
        }

        var usbCandidates = existing
            .Where(component => component.PartNumber.Contains("USB Type-C", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (proposed.PartNumber.Contains("USB Type-C", StringComparison.OrdinalIgnoreCase) && usbCandidates.Count > 1)
        {
            return Review(null, "USB Type-C 候选项不唯一，需要人工确认。");
        }

        return Create();
    }

    private static BomMatchResult Create() => new()
    {
        Action = BomMatchAction.Create,
        Confidence = 1d,
        Warnings = [],
    };

    private static BomMatchResult Merge(ComponentRecord component, double confidence) => new()
    {
        Action = BomMatchAction.Merge,
        Confidence = confidence,
        MatchedComponent = component,
        Warnings = [],
    };

    private static BomMatchResult Review(ComponentRecord? component, string warning) => new()
    {
        Action = BomMatchAction.Review,
        Confidence = 0d,
        MatchedComponent = component,
        Warnings = [warning],
    };

    private static bool SameModel(string left, string right) =>
        string.Equals(CanonicalModel(left), CanonicalModel(right), StringComparison.OrdinalIgnoreCase);

    private static bool SamePackage(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameCategory(string left, string right) =>
        string.Equals(CanonicalCategory(left), CanonicalCategory(right), StringComparison.Ordinal);

    private static bool IsSameManufacturerFamily(string left, string right)
    {
        var leftFamily = ManufacturerFamily(left);
        var rightFamily = ManufacturerFamily(right);
        return !string.IsNullOrEmpty(leftFamily) && string.Equals(leftFamily, rightFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalModel(string value) => LegacyPackageSuffix().Replace(value.Trim(), string.Empty).ToLowerInvariant();

    private static string CanonicalCategory(string value) => value.Trim() switch
    {
        "贴片电容" or "电容器" => "电容",
        "贴片电阻" or "电阻器" => "电阻",
        "贴片电感" => "电感",
        _ => value.Trim(),
    };

    private static string ManufacturerFamily(string partNumber) =>
        partNumber.Trim().TrimEnd('V', 'v');

    private static bool IsLegacyConcatenated(string partNumber) => LegacyPackageSuffix().IsMatch(partNumber);

    [GeneratedRegex("_[A-Za-z-]*\\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyPackageSuffix();
}
