using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class StoreAppsTests
{
    const string Family = "julienpiron.fr.NexusWpp_yq2nr3sn86fpg";

    // The display catalog's answer, cut to what is read (2026-11-02: Submission 33 served 1.0.51.0).
    static string Catalog(params string[] fullNames)
    {
        var packages = string.Join(",", fullNames.Select(name => $$"""{"PackageFullName":"{{name}}","PackageFormat":"Msix"}"""));
        return """{"Products":[{"DisplaySkuAvailabilities":[{"Sku":{"Properties":{"Packages":[""" + packages
            + """]}}},{"Sku":{"Properties":{}}}]}]}""";
    }

    [Fact]
    public void TheListingLinkGivesTheStoreId() => Assert.Equal("9N2VCVDH7QDX", StoreApps.ProductId(Products.Wpp));

    [Theory]
    [InlineData(null)]
    [InlineData("https://julienpiron.fr/nexuswpp/")]
    [InlineData("ms-windows-store://pdp/?productid=")]
    [InlineData("ms-windows-store://pdp/?productid=9N2V CVDH")]
    public void NoIdWithoutAProductId(string? url) => Assert.Null(StoreApps.ProductId(Products.Wpp with { StoreUrl = url }));

    [Fact]
    public void ThePublishedVersionIsTheServedPackagesHighest() =>
        Assert.Equal("1.0.51.0", StoreApps.PublishedVersion(Catalog(
            "julienpiron.fr.NexusWpp_1.0.45.0_x64__yq2nr3sn86fpg",
            "julienpiron.fr.NexusWpp_1.0.51.0_x64__yq2nr3sn86fpg",
            "julienpiron.fr.NexusWpp_1.0.9.0_arm64__yq2nr3sn86fpg"), Family));

    [Fact]
    public void AnotherPackageFamilyIsNotRead() =>
        Assert.Null(StoreApps.PublishedVersion(Catalog("julienpiron.fr.NexusOther_2.0.0.0_x64__yq2nr3sn86fpg", "julienpiron.fr.NexusWpp_9.0.0.0_x64__otherpublisher"), Family));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"Products":[]}""")]
    [InlineData("""{"Products":{"x":1}}""")]
    public void AnUnreadableAnswerIsNoVersion(string json) => Assert.Null(StoreApps.PublishedVersion(json, Family));

    [Fact]
    public void TheServedVersionAheadOfTheInstalledOneIsAnUpdate() =>
        Assert.True(ProductState.CompareVersions("1.0.51.0", "1.0.45.0") > 0);

    [Fact]
    public void TheUpdateForcesTheStorePackageSilently()
    {
        // winget's msstore source reports "Unknown" for an MSIX app: `upgrade` sees nothing, `install --force` takes it.
        var arguments = StoreApps.UpdateArguments("9N2VCVDH7QDX");
        Assert.StartsWith("install --id 9N2VCVDH7QDX --source msstore --force", arguments, StringComparison.Ordinal);
        Assert.Contains("--disable-interactivity", arguments, StringComparison.Ordinal);
        Assert.Contains("--accept-source-agreements", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentPackageHasNoInstalledVersion() =>
        Assert.Null(StoreApps.InstalledVersion(Products.Wpp with { PackageFamily = "julienpiron.fr.NotInstalled_yq2nr3sn86fpg" }));
}
