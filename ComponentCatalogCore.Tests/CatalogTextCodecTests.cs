using ComponentCatalogCore.Utilities;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogTextCodecTests
{
    [Fact]
    public void ParseList_SupportsPipeCommaAndNewlineSeparatedInput()
    {
        var values = CatalogTextCodec.ParseList("alias-1| alias-2,\nalias-3");

        Assert.Equal(["alias-1", "alias-2", "alias-3"], values);
    }

    [Fact]
    public void FormatList_JoinsValuesWithPipeSpacing()
    {
        var text = CatalogTextCodec.FormatList(["kw1", "kw2", "kw3"]);

        Assert.Equal("kw1 | kw2 | kw3", text);
    }
}
