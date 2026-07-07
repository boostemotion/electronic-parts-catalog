using ComponentCatalogCore.Importing;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogImportServiceTests
{
    [Fact]
    public void ParseCsvRows_ParsesValidRowsAndReportsRowLevelErrors()
    {
        const string csv = """
name,partNumber,category,package,aliases,keywords,owned,location,notes,attributes
AMS1117-3.3,AMS1117-3.3,电源,SOT223,1117|LDO,regulator|3.3v,true,A1,linear regulator,voltage=3.3V; current=800mA
,NO-NAME,电源,SOT23,,,true,A2,missing name,voltage=5V
10k 电阻,RES-10K-0603-1%,电阻,0603,,resistor|1%,true,C4,pull-up,resistance=10k; tolerance=1%
""";

        var parsed = CatalogImportService.ParseCsvRows(csv);

        Assert.Equal(2, parsed.ValidRows.Count);
        Assert.Single(parsed.Errors);
        Assert.Equal(3, parsed.Errors[0].Line);
        Assert.Equal("name is required", parsed.Errors[0].Reason);
        Assert.Equal("AMS1117-3.3", parsed.ValidRows[0].PartNumber);
        Assert.Equal("3.3V", parsed.ValidRows[0].Attributes["voltage"]);
        Assert.Equal(["1117", "LDO"], parsed.ValidRows[0].Aliases);
    }
}
