using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Taxonomy;

namespace ComponentCatalogCore.Importing;

public static class CatalogImportService
{
    public static ImportParseResult ParseCsvRows(string input)
    {
        var lines = input
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var headerLine = lines.FirstOrDefault() ?? string.Empty;
        var rows = lines.Skip(1).ToList();
        var headers = CsvValue(headerLine);
        var validRows = new List<ImportRow>();
        var errors = new List<ImportError>();

        for (var index = 0; index < rows.Count; index++)
        {
            var values = CsvValue(rows[index]);
            var record = headers
                .Select((header, headerIndex) => new { header, value = headerIndex < values.Count ? values[headerIndex] : string.Empty })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);

            if (!record.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportError { Line = index + 2, Reason = "name is required" });
                continue;
            }

            if (!record.TryGetValue("partNumber", out var partNumber) || string.IsNullOrWhiteSpace(partNumber))
            {
                errors.Add(new ImportError { Line = index + 2, Reason = "partNumber is required" });
                continue;
            }

            var package = record.GetValueOrDefault("package") ?? string.Empty;
            var majorCategoryId = record.GetValueOrDefault("majorCategoryId")?.Trim() ?? string.Empty;
            var subCategoryId = record.GetValueOrDefault("subCategoryId")?.Trim() ?? string.Empty;
            ComponentCategoryAssignment? assignment;
            if (string.IsNullOrEmpty(majorCategoryId) && string.IsNullOrEmpty(subCategoryId))
            {
                assignment = ComponentCategoryTaxonomy.ResolveLegacy(record.GetValueOrDefault("category"), package, partNumber);
            }
            else
            {
                assignment = ComponentCategoryTaxonomy.IsValidPair(majorCategoryId, subCategoryId)
                    ? ComponentCategoryTaxonomy.GetAssignment(majorCategoryId, subCategoryId)
                    : null;
            }

            if (assignment is null)
            {
                errors.Add(new ImportError { Line = index + 2, Reason = "category must resolve to a known taxonomy entry" });
                continue;
            }

            validRows.Add(new ImportRow
            {
                Name = name,
                PartNumber = partNumber,
                Category = assignment.SubCategory,
                MajorCategoryId = assignment.MajorCategoryId,
                SubCategoryId = assignment.SubCategoryId,
                Package = package,
                Owned = ParseBoolean(record.GetValueOrDefault("owned") ?? "false"),
                Attributes = ParseAttributes(record.GetValueOrDefault("attributes") ?? string.Empty),
                Aliases = SplitList(record.GetValueOrDefault("aliases") ?? string.Empty),
                Keywords = SplitList(record.GetValueOrDefault("keywords") ?? string.Empty),
                Location = record.GetValueOrDefault("location") ?? string.Empty,
                Notes = record.GetValueOrDefault("notes") ?? string.Empty,
                SourceType = ComponentSourceType.CsvImport,
            });
        }

        return new ImportParseResult
        {
            ValidRows = validRows,
            Errors = errors,
        };
    }

    public static IReadOnlyDictionary<string, string> ParseAttributes(string input)
    {
        return input
            .Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> CsvValue(string line)
    {
        var fields = new List<string>();
        var i = 0;
        while (i <= line.Length)
        {
            if (i == line.Length)
            {
                // 末尾空字段（行以逗号结尾时）
                fields.Add(string.Empty);
                break;
            }

            if (line[i] == '"')
            {
                // 引号字段：收集内容直到闭合引号
                i++; // 跳过起始引号
                var value = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            // "" 转义为 "
                            value.Append('"');
                            i += 2;
                        }
                        else
                        {
                            // 闭合引号
                            i++; // 跳过闭合引号
                            break;
                        }
                    }
                    else
                    {
                        value.Append(line[i]);
                        i++;
                    }
                }

                fields.Add(value.ToString().Trim());

                // 跳过逗号分隔符（或到达行尾）
                if (i < line.Length && line[i] == ',')
                    i++;
            }
            else
            {
                // 非引号字段：读取到下一个逗号
                var commaIndex = line.IndexOf(',', i);
                if (commaIndex < 0)
                {
                    fields.Add(line[i..].Trim());
                    break;
                }
                else
                {
                    fields.Add(line[i..commaIndex].Trim());
                    i = commaIndex + 1;
                }
            }
        }

        return fields;
    }

    private static bool ParseBoolean(string value) => Normalize(value) is "true" or "1" or "yes" or "y";

    private static IReadOnlyList<string> SplitList(string value)
    {
        return value
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
