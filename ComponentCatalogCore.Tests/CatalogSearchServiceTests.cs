using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Search;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogSearchServiceTests
{
    [Fact]
    public void Search_PrioritizesExactPartNumberOverAliasAndKeywordMatches()
    {
        var components = new[]
        {
            new ComponentRecord
            {
                Id = "exact",
                Name = "AMS1117-3.3",
                PartNumber = "AMS1117-3.3",
                Category = "电源",
                Package = "SOT223",
                Aliases = [],
                Keywords = ["ldo"],
                Attributes = new Dictionary<string, string> { ["voltage"] = "3.3V" },
                Owned = true,
                Location = "A1",
                Substitutes = [],
                Notes = "exact",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "alias",
                Name = "Low Dropout Regulator",
                PartNumber = "REG-33",
                Category = "电源",
                Package = "SOT23",
                Aliases = ["AMS1117-3.3"],
                Keywords = ["ldo"],
                Attributes = new Dictionary<string, string> { ["voltage"] = "3.3V" },
                Owned = true,
                Location = "A2",
                Substitutes = [],
                Notes = "alias",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "keyword",
                Name = "Generic Regulator",
                PartNumber = "REG-ALT",
                Category = "电源",
                Package = "SOT23",
                Aliases = [],
                Keywords = ["ams1117-3.3", "ldo"],
                Attributes = new Dictionary<string, string> { ["voltage"] = "3.3V" },
                Owned = true,
                Location = "A3",
                Substitutes = [],
                Notes = "keyword",
                SourceType = ComponentSourceType.Manual,
            },
        };

        var results = CatalogSearchService.Search(
            components,
            new SearchQuery
            {
                Text = "AMS1117-3.3",
                Category = "全部",
            });

        Assert.Collection(
            results,
            item => Assert.Equal("exact", item.Component.Id),
            item => Assert.Equal("alias", item.Component.Id),
            item => Assert.Equal("keyword", item.Component.Id));
    }

    [Fact]
    public void Search_CombinesTokensAcrossNamePackageAndAttributes()
    {
        var components = new[]
        {
            new ComponentRecord
            {
                Id = "match",
                Name = "LDO 稳压器",
                PartNumber = "ME6211",
                Category = "电源",
                Package = "SOT23",
                Aliases = [],
                Keywords = ["ldo"],
                Attributes = new Dictionary<string, string>
                {
                    ["voltage"] = "3.3V",
                    ["current"] = "500mA",
                },
                Owned = true,
                Location = "B1",
                Substitutes = [],
                Notes = "适合低功耗板子",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "no-match",
                Name = "Buck 电源",
                PartNumber = "MP1584",
                Category = "电源",
                Package = "SOIC",
                Aliases = [],
                Keywords = [],
                Attributes = new Dictionary<string, string> { ["voltage"] = "5V" },
                Owned = true,
                Location = "B2",
                Substitutes = [],
                Notes = "不匹配",
                SourceType = ComponentSourceType.Manual,
            },
        };

        var results = CatalogSearchService.Search(
            components,
            new SearchQuery
            {
                Text = "3.3V LDO SOT23",
                Category = "全部",
            });

        var result = Assert.Single(results);
        Assert.Equal("match", result.Component.Id);
        Assert.Contains("package", result.MatchedFields);
        Assert.Contains("attributes", result.MatchedFields);
    }

    [Fact]
    public void Search_FiltersByPackageOwnedAndAttributeText()
    {
        var components = new[]
        {
            new ComponentRecord
            {
                Id = "owned-match",
                Name = "ME6211",
                PartNumber = "ME6211C33",
                Category = "电源",
                Package = "SOT23",
                Aliases = [],
                Keywords = ["ldo"],
                Attributes = new Dictionary<string, string>
                {
                    ["voltage"] = "3.3V",
                    ["current"] = "500mA",
                },
                Owned = true,
                Location = "B1",
                Substitutes = [],
                Notes = "match",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "wrong-attribute",
                Name = "ME6211 5V",
                PartNumber = "ME6211C50",
                Category = "电源",
                Package = "SOT23",
                Aliases = [],
                Keywords = ["ldo"],
                Attributes = new Dictionary<string, string>
                {
                    ["voltage"] = "5V",
                },
                Owned = true,
                Location = "B2",
                Substitutes = [],
                Notes = "wrong voltage",
                SourceType = ComponentSourceType.Manual,
            },
            new ComponentRecord
            {
                Id = "wrong-owned",
                Name = "ME6211 spare",
                PartNumber = "ME6211-SPARE",
                Category = "电源",
                Package = "SOT23",
                Aliases = [],
                Keywords = ["ldo"],
                Attributes = new Dictionary<string, string>
                {
                    ["voltage"] = "3.3V",
                },
                Owned = false,
                Location = "B3",
                Substitutes = [],
                Notes = "not owned",
                SourceType = ComponentSourceType.Manual,
            },
        };

        var results = CatalogSearchService.Search(
            components,
            new SearchQuery
            {
                Text = "ldo",
                Category = "电源",
                Package = "SOT23",
                Owned = true,
                AttributeText = "voltage=3.3V",
            });

        var result = Assert.Single(results);
        Assert.Equal("owned-match", result.Component.Id);
    }
}
