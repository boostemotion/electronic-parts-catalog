using ComponentCatalogCore.Transport;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogNetworkPortsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10849)]
    [InlineData(65535)]
    public void IsValid_AcceptsPortsInRange(int port)
    {
        Assert.True(CatalogNetworkPorts.IsValid(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void IsValid_RejectsPortsOutsideRange(int port)
    {
        Assert.False(CatalogNetworkPorts.IsValid(port));
    }

    [Fact]
    public void IsValidPair_RequiresDistinctValidPorts()
    {
        Assert.True(CatalogNetworkPorts.IsValidPair(10849, 10850));
        Assert.False(CatalogNetworkPorts.IsValidPair(10849, 10849));
        Assert.False(CatalogNetworkPorts.IsValidPair(0, 10850));
    }

    [Fact]
    public void ValidatePair_ThrowsForInvalidOrEqualPorts()
    {
        var outOfRange = Assert.Throws<ArgumentOutOfRangeException>(() => CatalogNetworkPorts.ValidatePair(0, 10850));
        Assert.Equal("apiPort", outOfRange.ParamName);

        var equal = Assert.Throws<ArgumentException>(() => CatalogNetworkPorts.ValidatePair(10849, 10849));
        Assert.Contains("different", equal.Message, StringComparison.OrdinalIgnoreCase);

        CatalogNetworkPorts.ValidatePair(18000, 18001);
    }
}
