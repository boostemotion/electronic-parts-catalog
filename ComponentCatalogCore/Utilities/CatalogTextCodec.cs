namespace ComponentCatalogCore.Utilities;

public static class CatalogTextCodec
{
    public static IReadOnlyList<string> ParseList(string input)
    {
        return input
            .Split(['|', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatList(IEnumerable<string> values)
    {
        return string.Join(" | ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
    }
}
