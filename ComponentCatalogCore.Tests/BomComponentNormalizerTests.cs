using ComponentCatalogCore.Importing;

namespace ComponentCatalogCore.Tests;

public sealed class BomComponentNormalizerTests
{
    [Fact]
    public void Normalize_SeparatesCapacitorValueAndPackage()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "100nf",
            Footprint = "C0603",
            Designators = ["C3", "C4"],
        });

        Assert.Equal("100nF", result.Record.PartNumber);
        Assert.Equal("100nF", result.Record.Name);
        Assert.Equal("0603", result.Record.Package);
        Assert.Equal("贴片电容(MLCC)", result.Record.Category);
        Assert.Equal("passives", result.Record.MajorCategoryId);
        Assert.Equal("mlcc", result.Record.SubCategoryId);
    }

    [Fact]
    public void Normalize_SeparatesResistorValueAndPackage()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "10K",
            Footprint = "R0805",
            Designators = ["R1"],
        });

        Assert.Equal("10k", result.Record.PartNumber);
        Assert.Equal("0805", result.Record.Package);
        Assert.Equal("贴片电阻", result.Record.Category);
    }

    [Fact]
    public void Normalize_SeparatesInductorValueAndPackage()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "10uH",
            Footprint = "IND-SMD-0630",
            Designators = ["L1"],
        });

        Assert.Equal("10uH", result.Record.PartNumber);
        Assert.Equal("0630", result.Record.Package);
        Assert.Equal("贴片电感", result.Record.Category);
    }

    [Fact]
    public void Normalize_UsesExplicitValueAsDisplayNameWhilePreservingManufacturerPart()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "10uF",
            Footprint = "C1206",
            Designators = ["C1"],
            ManufacturerPart = "CL31A106KAHNNNE",
        });

        Assert.Equal("CL31A106KAHNNNE", result.Record.PartNumber);
        Assert.Equal("10uF", result.Record.Name);
        Assert.Equal("1206", result.Record.Package);
        Assert.Equal("10uF", result.Record.Attributes["value"]);
        Assert.Equal("C1206", result.Record.Attributes["footprint"]);
    }

    [Fact]
    public void Normalize_FallsBackToManufacturerPartWhenValueIsMissing()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Footprint = "QFN-24",
            Designators = ["U1"],
            ManufacturerPart = "FD6288Q",
        });

        Assert.Equal("FD6288Q", result.Record.Name);
        Assert.Equal("FD6288Q", result.Record.PartNumber);
    }

    [Fact]
    public void Normalize_DoesNotConcatenateValueAndPackage_AndKeeps105KDistinct()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "105K",
            Footprint = "R0603",
            Designators = ["R1"],
        });

        Assert.Equal("105k", result.Record.PartNumber);
        Assert.Equal("0603", result.Record.Package);
        Assert.DoesNotContain("_R0603", result.Record.PartNumber, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_R0603", result.Record.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_AppliesSparseEvidenceBasedMetadataRules()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "100nf",
            Footprint = "C0603",
            Designators = ["C1"],
            Keywords = [" decoupling ", "DECOUPLING", "100nF", "0603", "电容", "bypass", "bulk", "ceramic", "extra"],
            Attributes = new Dictionary<string, string>
            {
                ["voltage"] = "16V",
                ["dielectric"] = "X7R",
                ["tolerance"] = "10%",
                ["supplier"] = "Example",
                ["unverified"] = "not supplied",
            },
            Substitutes = ["invented-alternative"],
            Notes = "  A short decoupling capacitor.  ",
        });

        Assert.Equal(["decoupling", "电容", "bypass", "bulk", "ceramic"], result.Record.Keywords);
        Assert.Equal(5, result.Record.Attributes.Count);
        Assert.Equal("C0603", result.Record.Attributes["footprint"]);
        Assert.Empty(result.Record.Substitutes);
        Assert.Equal("A short decoupling capacitor.", result.Record.Notes);
        Assert.Contains(result.Warnings, warning => warning.Contains("替代", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalize_UsesOtherAndWarnsForUnknownCategory()
    {
        var result = BomComponentNormalizer.Normalize(new BomImportRow
        {
            Value = "Widget",
            Footprint = "Custom",
            Category = "电容器",
        });

        Assert.Equal("未分类", result.Record.Category);
        Assert.Equal("other", result.Record.MajorCategoryId);
        Assert.Equal("uncategorized", result.Record.SubCategoryId);
        Assert.NotEmpty(result.Warnings);
    }
}
