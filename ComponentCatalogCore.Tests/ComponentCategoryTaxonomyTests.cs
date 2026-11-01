using System.Text.Json;
using ComponentCatalogCore;
using ComponentCatalogCore.Taxonomy;

namespace ComponentCatalogCore.Tests;

public sealed class ComponentCategoryTaxonomyTests
{
    [Fact]
    public void Taxonomy_DefinesAllFourteenOrderedMajorCategoriesWithUniqueIds()
    {
        Assert.Equal(14, ComponentCategoryTaxonomy.MajorCategories.Count);
        Assert.Equal(
        [
            "passives", "mcu-logic", "diodes-transistors", "protection-power", "interface-timing",
            "connectors-switches", "data-rf", "opto-display", "filters-oscillators",
            "memory-sensors-isolation", "relays-audio-motors", "modules-communication",
            "amplifiers-development", "other",
        ],
        ComponentCategoryTaxonomy.MajorCategories.Select(category => category.Id));
        Assert.Equal(14, ComponentCategoryTaxonomy.MajorCategories.Select(category => category.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Taxonomy_DefinesGloballyUniqueSubcategoriesAttachedToKnownMajors()
    {
        var subcategories = ComponentCategoryTaxonomy.MajorCategories.SelectMany(category => category.Subcategories).ToList();

        Assert.Equal(subcategories.Count, subcategories.Select(category => category.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(subcategories, category => Assert.NotNull(ComponentCategoryTaxonomy.FindMajor(category.MajorCategoryId)));
    }

    [Fact]
    public void Taxonomy_LooksUpFixedLabelsAndRejectsUnknownIds()
    {
        var category = ComponentCategoryTaxonomy.FindSubcategory("chip-resistor");

        Assert.NotNull(category);
        Assert.Equal("passives", category.MajorCategoryId);
        Assert.Equal("贴片电阻", category.Label);
        Assert.Equal("电容 / 电阻 / 电感", ComponentCategoryTaxonomy.FindMajor("passives")!.Label);
        Assert.Null(ComponentCategoryTaxonomy.FindMajor("not-a-major"));
        Assert.Null(ComponentCategoryTaxonomy.FindSubcategory("not-a-subcategory"));
        Assert.False(ComponentCategoryTaxonomy.IsValidPair("passives", "ldo"));
    }

    [Theory]
    [InlineData("电阻", "0603", "passives", "chip-resistor")]
    [InlineData("贴片电阻", "", "passives", "chip-resistor")]
    [InlineData("贴片电容", "1206", "passives", "mlcc")]
    [InlineData("微控制器(MCU)", "LQFP48", "mcu-logic", "mcu-mpu-soc")]
    public void Taxonomy_MapsLegacyAliases(string category, string package, string majorCategoryId, string subCategoryId)
    {
        var result = ComponentCategoryTaxonomy.ResolveLegacy(category, package);

        Assert.NotNull(result);
        Assert.Equal(majorCategoryId, result.MajorCategoryId);
        Assert.Equal(subCategoryId, result.SubCategoryId);
    }

    [Fact]
    public void Taxonomy_SerializesDeterministically()
    {
        var first = JsonSerializer.Serialize(ComponentCategoryTaxonomy.Definition, CatalogJson.DefaultOptions);
        var second = JsonSerializer.Serialize(ComponentCategoryTaxonomy.Definition, CatalogJson.DefaultOptions);

        Assert.Equal(first, second);
        Assert.Contains("\"version\": \"1\"", first);
        Assert.Contains("\"chip-resistor\"", first);
    }
}
