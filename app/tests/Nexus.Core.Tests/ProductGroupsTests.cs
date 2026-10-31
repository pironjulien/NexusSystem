using Xunit;
using Nexus.Core;

namespace Nexus.Core.Tests;

public class ProductGroupsTests
{
    [Fact]
    public void EveryProductHasOneWayIn()
    {
        // The AI products install through the AI apps; a store app only through its store; a private one not yet.
        Assert.All(Products.All.Where(p => p.Category == Category.Ai), p => Assert.Null(p.StoreUrl));
        Assert.Equal([Products.InDesign], Products.All.Where(p => p.Category == Category.Adobe));
        Assert.Equal([Products.Wpp], Products.All.Where(p => p.Category == Category.Windows));
        Assert.Equal(Availability.OtherStore, Products.Wpp.Availability);
        Assert.StartsWith("ms-windows-store://", Products.Wpp.StoreUrl);
        Assert.Equal(Availability.NotPublic, Products.InDesign.Availability);
        Assert.Null(Products.InDesign.PageFor(true));
    }

    [Fact]
    public void SitePageFollowsTheLanguage()
    {
        Assert.Equal("https://julienpiron.fr/nexuswpp/", Products.Wpp.PageFor(french: true));
        Assert.Equal("https://julienpiron.fr/en/nexuswpp/", Products.Wpp.PageFor(french: false));
    }

    [Fact]
    public void StoreAppIsInstalledWhenWindowsHasItsPackageFolder()
    {
        using var profile = new FakeProfile();
        Assert.False(ProductUpdates.IsInstalled(Products.Wpp, [], profile.Folders));
        Directory.CreateDirectory(Path.Combine(profile.Folders.LocalAppData, "Packages", Products.Wpp.PackageFamily!));
        Assert.True(ProductUpdates.IsInstalled(Products.Wpp, [], profile.Folders));
    }
}
