using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Validation;

namespace ComponentCatalogCore.Tests;

public sealed class ComponentRecordValidatorTests
{
    [Fact]
    public void ValidateAndNormalize_MapsLegacyCategoryAndDerivesDisplayCategory()
    {
        var component = CreateComponent();
        component.Category = "贴片电阻";
        component.Package = "0603";

        var error = ComponentRecordValidator.ValidateAndNormalize(component);

        Assert.Null(error);
        Assert.Equal("passives", component.MajorCategoryId);
        Assert.Equal("chip-resistor", component.SubCategoryId);
        Assert.Equal("贴片电阻", component.Category);
    }

    [Fact]
    public void ValidateAndNormalize_UsesStableIdsAsTheCategorySourceOfTruth()
    {
        var component = CreateComponent();
        component.Category = "arbitrary old text";
        component.MajorCategoryId = "protection-power";
        component.SubCategoryId = "ldo";

        var error = ComponentRecordValidator.ValidateAndNormalize(component);

        Assert.Null(error);
        Assert.Equal("线性稳压器(LDO)", component.Category);
    }

    [Theory]
    [InlineData("not-real", "ldo")]
    [InlineData("passives", "ldo")]
    public void ValidateAndNormalize_RejectsUnknownOrMismatchedStableIds(string majorCategoryId, string subCategoryId)
    {
        var component = CreateComponent();
        component.MajorCategoryId = majorCategoryId;
        component.SubCategoryId = subCategoryId;

        var error = ComponentRecordValidator.ValidateAndNormalize(component);

        Assert.Equal("MajorCategoryId and SubCategoryId must be a known taxonomy pair.", error);
    }

    [Fact]
    public void ValidateAndNormalize_RejectsUnknownFreeTextCategory()
    {
        var component = CreateComponent();
        component.Category = "custom category";

        var error = ComponentRecordValidator.ValidateAndNormalize(component);

        Assert.Equal("Category must resolve to a known taxonomy entry.", error);
    }

    private static ComponentRecord CreateComponent() => new()
    {
        Id = "record",
        Name = "Example",
        PartNumber = "EXAMPLE",
        Category = "",
        Package = "0603",
        Aliases = [],
        Keywords = [],
        Attributes = new Dictionary<string, string>(),
        Substitutes = [],
        SourceType = ComponentSourceType.Manual,
    };
}
