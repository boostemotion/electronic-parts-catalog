using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Importing;

namespace ComponentCatalogCore.Tests;

public sealed class BomComponentMatcherTests
{
    [Fact]
    public void Match_PassiveMatchesLegacyConcatenatedMigrationCandidate()
    {
        var result = BomComponentMatcher.Match(
            Component("new", "100nF", "电容", "0603"),
            hasManufacturerPart: false,
            [Component("legacy", "100nf_C0603", "贴片电容", "0603")]);

        Assert.Equal(BomMatchAction.Merge, result.Action);
        Assert.Equal("legacy", result.MatchedComponent?.Id);
    }

    [Fact]
    public void Match_PassiveDoesNotMatchDifferentPackage()
    {
        var result = BomComponentMatcher.Match(
            Component("new", "10k", "电阻", "0603"),
            hasManufacturerPart: false,
            [Component("legacy", "10k", "电阻", "0805")]);

        Assert.Equal(BomMatchAction.Create, result.Action);
    }

    [Fact]
    public void Match_ManufacturerPartWithDifferentPackageRequiresReview()
    {
        var result = BomComponentMatcher.Match(
            Component("new", "AMS1117-3.3", "电源", "SOT223"),
            hasManufacturerPart: true,
            [Component("legacy", "AMS1117-3.3", "电源", "SOT89")]);

        Assert.Equal(BomMatchAction.Review, result.Action);
    }

    [Fact]
    public void Match_NearManufacturerPartRequiresReviewInsteadOfExactMerge()
    {
        var result = BomComponentMatcher.Match(
            Component("new", "AMS1117-3.3V", "电源", "SOT223"),
            hasManufacturerPart: true,
            [Component("legacy", "AMS1117-3.3", "电源", "SOT223")]);

        Assert.Equal(BomMatchAction.Review, result.Action);
    }

    [Fact]
    public void Match_AmbiguousUsbTypeCRequiresReview()
    {
        var result = BomComponentMatcher.Match(
            Component("new", "USB Type-C", "连接器", ""),
            hasManufacturerPart: false,
            [
                Component("one", "USB Type-C-16P", "连接器", ""),
                Component("two", "USB Type-C-24P", "连接器", ""),
            ]);

        Assert.Equal(BomMatchAction.Review, result.Action);
    }

    [Fact]
    public void Match_ExactManufacturerPartAndPackageMergesWithHighConfidence()
    {
        var result = BomComponentMatcher.Match(
            Component("new", "STM32F103C8T6", "集成电路", "LQFP48"),
            hasManufacturerPart: true,
            [Component("existing", "stm32f103c8t6", "MCU", "lqfp48")]);

        Assert.Equal(BomMatchAction.Merge, result.Action);
        Assert.Equal("existing", result.MatchedComponent?.Id);
        Assert.Equal(1d, result.Confidence);
    }

    private static ComponentRecord Component(string id, string partNumber, string category, string package) => new()
    {
        Id = id,
        Name = partNumber,
        PartNumber = partNumber,
        Category = category,
        Package = package,
        Aliases = [],
        Keywords = [],
        Attributes = new Dictionary<string, string>(),
        Owned = true,
        Location = string.Empty,
        Substitutes = [],
        Notes = string.Empty,
        SourceType = ComponentSourceType.Manual,
    };
}
