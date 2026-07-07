using ComponentCatalogCore.Contracts;

namespace ComponentCatalogCore.Search;

public static class CatalogSearchService
{
    public static IReadOnlyList<SearchResult> Search(IEnumerable<ComponentRecord> components, SearchQuery query)
    {
        return components
            .Select(component => CalculateScore(component, query))
            .Where(result => result is not null)
            .Cast<SearchResult>()
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Component.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static SearchResult? CalculateScore(ComponentRecord component, SearchQuery query)
    {
        var normalizedText = Normalize(query.Text);
        var tokens = Tokenize(query.Text);
        var matchedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 0;

        if (!string.Equals(query.Category, "全部", StringComparison.Ordinal) &&
            !string.Equals(component.Category, query.Category, StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(query.Package) &&
            !string.Equals(component.Package, query.Package, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (query.Owned.HasValue && component.Owned != query.Owned.Value)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(query.AttributeText) &&
            !MatchesAttributeFilter(component, query.AttributeText))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return new SearchResult
            {
                Component = component,
                Score = 1,
                MatchedFields = ["category"],
            };
        }

        if (Normalize(component.PartNumber) == normalizedText)
        {
            score += 1000;
            matchedFields.Add("partNumber");
        }

        foreach (var alias in component.Aliases)
        {
            if (Normalize(alias).Contains(normalizedText, StringComparison.Ordinal))
            {
                score += 500;
                matchedFields.Add("aliases");
            }
        }

        foreach (var field in CreateSearchHaystack(component))
        {
            if (field.Contains(normalizedText, StringComparison.Ordinal))
            {
                score += 80;
                matchedFields.Add("keywords");
            }
        }

        foreach (var token in tokens)
        {
            if (Normalize(component.Package).Contains(token, StringComparison.Ordinal))
            {
                score += 50;
                matchedFields.Add("package");
            }

            if (component.Attributes.Any(pair => Normalize($"{pair.Key} {pair.Value}").Contains(token, StringComparison.Ordinal)))
            {
                score += 40;
                matchedFields.Add("attributes");
            }

            if (Normalize(component.Name).Contains(token, StringComparison.Ordinal) ||
                Normalize(component.PartNumber).Contains(token, StringComparison.Ordinal))
            {
                score += 30;
                matchedFields.Add("name");
            }
        }

        if (score == 0)
        {
            return null;
        }

        return new SearchResult
        {
            Component = component,
            Score = score,
            MatchedFields = matchedFields.ToList(),
        };
    }

    private static IEnumerable<string> CreateSearchHaystack(ComponentRecord component)
    {
        return new[]
        {
            component.Name,
            component.PartNumber,
            component.Package,
            component.Category,
        }
        .Concat(component.Aliases)
        .Concat(component.Keywords)
        .Concat(component.Attributes.SelectMany(pair => new[] { pair.Key, pair.Value }))
        .Select(Normalize);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return Normalize(value)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool MatchesAttributeFilter(ComponentRecord component, string attributeText)
    {
        var normalizedFilter = Normalize(attributeText);
        if (string.IsNullOrWhiteSpace(normalizedFilter))
        {
            return true;
        }

        var separatorIndex = normalizedFilter.IndexOf('=');
        if (separatorIndex >= 0)
        {
            var key = normalizedFilter[..separatorIndex].Trim();
            var value = normalizedFilter[(separatorIndex + 1)..].Trim();
            return component.Attributes.Any(pair =>
                Normalize(pair.Key) == key &&
                Normalize(pair.Value).Contains(value, StringComparison.Ordinal));
        }

        return component.Attributes.Any(pair =>
            Normalize(pair.Key).Contains(normalizedFilter, StringComparison.Ordinal) ||
            Normalize(pair.Value).Contains(normalizedFilter, StringComparison.Ordinal) ||
            Normalize($"{pair.Key}={pair.Value}").Contains(normalizedFilter, StringComparison.Ordinal));
    }
}
