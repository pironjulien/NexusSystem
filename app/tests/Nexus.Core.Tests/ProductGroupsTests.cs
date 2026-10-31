using Xunit;
using Nexus.Core;

namespace Nexus.Core.Tests;

public class ProductGroupsTests
{
    // The build output lives outside the repository (~\.nexusuild): found from this source file instead.
    static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        for (var folder = new FileInfo(source).Directory; folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "products.json"))) return folder.FullName;
        throw new FileNotFoundException("products.json");
    }

    [Fact]
    public void EveryProductHasOneWayIn()
    {
        // The AI products install through the AI apps; a store app only through its store; a private one not yet.
        Assert.All(Products.All.Where(p => p.Group == "ai"), p => Assert.Null(p.StoreUrl));
        Assert.Equal([Products.InDesign], Products.All.Where(p => p.Group == "adobe"));
        Assert.Equal([Products.Wpp], Products.All.Where(p => p.Group == "windows"));
        Assert.Equal(Availability.OtherStore, Products.Wpp.Availability);
        Assert.StartsWith("ms-windows-store://", Products.Wpp.StoreUrl);
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

    [Fact]
    public void PublishedCatalogShowsEveryBuiltInProduct()
    {
        // The online catalog is this repository's products.json: it must stay readable and keep what the code knows.
        var catalog = ProductCatalog.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "products.json")));
        Assert.NotNull(catalog);
        Assert.Equal(Products.All.Select(p => p.Key).Order(), catalog.Products.Select(p => p.Key).Order());
        foreach (var product in catalog.Products)
        {
            var known = Products.ByKey(product.Key);
            Assert.Equal((known.Availability, known.Group, known.StoreUrl, known.PackageFamily, known.Server),
                (product.Availability, product.Group, product.StoreUrl, product.PackageFamily, product.Server));
            Assert.NotNull(product.AboutFr);
            Assert.NotNull(product.AboutEn);
        }
    }

    [Fact]
    public void CatalogAddsMovesAndReleasesProducts()
    {
        var catalog = ProductCatalog.Parse("""
            {"schema": 1,
             "groups": [{"key": "ai", "fr": "Produits IA", "en": "AI products"}, {"key": "office", "fr": "Produits Office", "en": "Office products"}],
             "products": [
               {"key": "wpp", "group": "office", "state": "store", "store": "ms-windows-store://pdp/?productid=9N2VCVDH7QDX"},
               {"key": "newtool", "name": "NexusNew", "group": "ai", "state": "ready", "accent": "#123456", "server": "nexus-new",
                "targets": ["claude-code", "codex", "unknown"], "icon": "https://julienpiron.fr/nexusnew/icon.png",
                "about": {"fr": "Nouveau.", "en": "New."}},
               {"key": "other", "name": "NexusOther", "group": "ai", "state": "ready", "accent": "#123456"},
               {"key": "evil", "name": "NexusEvil", "group": "ai", "state": "store", "accent": "#123456", "store": "file:///C:/Windows/System32/calc.exe"},
               {"key": "lost", "name": "NexusLost", "group": "nowhere", "state": "soon", "accent": "#123456"}
             ]}
            """);
        Assert.NotNull(catalog);
        Assert.Equal(["wpp", "newtool"], catalog.Products.Select(p => p.Key));
        Assert.Equal(["ai", "office"], catalog.Groups.Select(g => g.Key));
        var wpp = catalog.ByKey("wpp")!;
        Assert.Equal(("office", "NexusWpp", "julienpiron.fr.NexusWpp_yq2nr3sn86fpg"), (wpp.Group, wpp.Name, wpp.PackageFamily));
        var added = catalog.ByKey("newtool")!;
        Assert.Equal((Availability.Ready, "nexus-new", "New."), (added.Availability, added.Server, added.AboutEn));
        Assert.Equal([AiApp.ClaudeCode, AiApp.Codex], added.Targets);
        // A product added by the catalog is in the AI prompt like a built-in one.
        Assert.Contains("-Only newtool", Products.AiPrompt(["newtool"], among: catalog.Products));
    }

    [Fact]
    public void InvalidCatalogKeepsTheBuiltInOne()
    {
        Assert.Null(ProductCatalog.Parse("not json"));
        Assert.Null(ProductCatalog.Parse("""{"schema": 2, "groups": [], "products": []}"""));
        using var profile = new FakeProfile();
        Assert.Same(ProductCatalog.BuiltIn, ProductCatalog.Load(profile.Folders));
        profile.Write(@".nexus\nexussystem\products.json", "{broken");
        Assert.Same(ProductCatalog.BuiltIn, ProductCatalog.Load(profile.Folders));
    }
}
