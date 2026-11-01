using ComponentCatalogCore.Transport;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogApiBindingOptionsTests
{
    [Fact]
    public void DefaultOptions_ExposeOnlyLoopbackPrefix()
    {
        var options = new CatalogApiBindingOptions();

        Assert.Equal(["http://127.0.0.1:10849/"], options.Prefixes);
        Assert.Equal(["http://127.0.0.1:10849"], options.BaseUrls);
        Assert.Equal("http://127.0.0.1:10849", options.BaseUrl);
    }

    [Fact]
    public void LanOptions_ExposeLoopbackAndSelectedPrivateAddressWithoutMutablePrefixes()
    {
        var options = new CatalogApiBindingOptions(10849, lanEnabled: true, lanIpv4: "192.168.8.25");

        Assert.Equal(
            ["http://127.0.0.1:10849/", "http://192.168.8.25:10849/"],
            options.Prefixes);
        Assert.Equal(
            ["http://127.0.0.1:10849", "http://192.168.8.25:10849"],
            options.BaseUrls);
        Assert.False(options.Prefixes is string[]);
        Assert.False(options.BaseUrls is string[]);
    }

    [Fact]
    public void LanOptions_ExposeOnlyAnExplicitMobileSnapshotUrl()
    {
        var options = new CatalogApiBindingOptions(10849, lanEnabled: true, lanIpv4: "192.168.8.25");

        Assert.Equal("http://192.168.8.25:10849/mobile/snapshot", options.MobileSnapshotUrl);
        Assert.DoesNotContain(options.Prefixes, prefix => prefix.Contains("+", StringComparison.Ordinal) || prefix.Contains("*", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("fe80::1")]
    [InlineData("catalog.example.test")]
    [InlineData("not-an-address")]
    public void Options_RejectUnsafeLanAddressesEvenWhenLanIsDisabled(string address)
    {
        Assert.Throws<ArgumentException>(() => new CatalogApiBindingOptions(10849, lanEnabled: false, lanIpv4: address));
    }
}
