using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class MaintenanceUpdatesTests
{
    [Fact]
    public void MissingReceiptIsUnknownAndDoesNotEnableAnUpdater()
    {
        using var profile = new FakeProfile();
        Assert.Null(MaintenanceUpdates.Read(profile.Folders));
        Assert.False(MaintenanceUpdates.IsInstalled(profile.Folders));
        Assert.False(ProductUpdates.CanRequest(Products.Windows, profile.Folders));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"schema\":\"1\",\"state\":\"success\"}")]
    [InlineData("{\"schema\":2,\"state\":\"success\"}")]
    [InlineData("{\"schema\":1}")]
    public void IncompleteOrUnknownSchemaIsNotSuccess(string json)
    {
        using var profile = new FakeProfile();
        profile.Write(@".nexus\status\nexussystem-update.json", json);
        Assert.Null(MaintenanceUpdates.Read(profile.Folders));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("checking")]
    public void KeepsOutcomeWithoutPromotingInstalledFilesToLoaded(string state)
    {
        using var profile = new FakeProfile();
        profile.Write(@".nexus\status\nexussystem-update.json", $$"""
            {"schema":1,"state":"{{state}}","activationPending":true,"retryable":true,
             "products":[{"key":"browser","publishedVersion":"2.33.5","installedVersion":"2.33.4","state":"pending",
               "profiles":[{"key":"codex","pluginVersion":"2.33.5","state":"pending","activationPending":true,"reason":"host reload required"}]}]}
            """);
        var receipt = Assert.IsType<MaintenanceState>(MaintenanceUpdates.Read(profile.Folders));
        Assert.Equal(state, receipt.State);
        Assert.True(receipt.ActivationPending);
        Assert.True(receipt.Retryable);
        var product = Assert.Single(receipt.Products);
        Assert.Equal("2.33.5", product.PublishedVersion);
        Assert.Equal("2.33.4", product.InstalledVersion);
        Assert.Empty(product.LoadedVersions);
        var plugin = Assert.Single(product.Profiles);
        Assert.Equal("2.33.5", plugin.PluginVersion);
        Assert.True(plugin.ActivationPending);
        Assert.False(receipt.IsFresh(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ReadsDatedRuntimeObservationsAndIgnoresMalformedOptionalEntries()
    {
        using var profile = new FakeProfile();
        var now = DateTimeOffset.UtcNow;
        profile.Write(@".nexus\status\nexussystem-update.json", $$"""
            {"schema":1,"state":"pending","checked_at":"{{now:o}}","nextCheckAt":"{{now.AddHours(1):o}}",
             "products":[null,{}, {"key":"windows","state":"pending","loadedVersions":["3.9.4",null,2,"3.9.4","3.9.2"],"profiles":[null,{}]}]}
            """);
        var receipt = Assert.IsType<MaintenanceState>(MaintenanceUpdates.Read(profile.Folders));
        Assert.Equal(now, receipt.CheckedAt);
        Assert.Equal(now.AddHours(1), receipt.NextCheckAt);
        var product = Assert.Single(receipt.Products);
        Assert.Equal(["3.9.4", "3.9.2"], product.LoadedVersions);
        Assert.Empty(product.Profiles);
        Assert.True(receipt.IsFresh(now));
        Assert.False(receipt.IsFresh(now.AddHours(3)));
        Assert.False(receipt.IsFresh(now.AddDays(2)));
        Assert.False(receipt.IsFresh(now.AddMinutes(-1)));
    }

    [Fact]
    public void FixedScheduledRequestDoesNotLaunchAnInstallerOrAnAi()
    {
        var request = MaintenanceUpdates.RequestCommand();
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), request.FileName);
        Assert.Equal(["/Run", "/TN", "NexusSystem Update"], request.ArgumentList);
        Assert.False(request.UseShellExecute);
        Assert.True(request.CreateNoWindow);
    }

    [Fact]
    public async Task CancellationBeforeRequestDoesNotStartMaintenance()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Equal(UpdateRequest.Unreachable, await MaintenanceUpdates.RequestAsync(cancel.Token));
    }

    [Fact]
    public void InstalledWorkerEnablesCoordinatedBrowserAndWindowsRequests()
    {
        using var profile = new FakeProfile();
        profile.Write(@".nexus\nexussystem\maintenance\get-nexus.ps1", "# installed maintenance worker");
        Assert.True(ProductUpdates.CanRequest(Products.Windows, profile.Folders));
        Assert.True(ProductUpdates.CanRequest(Products.Browser, profile.Folders));
        Assert.False(ProductUpdates.CanRequest(Products.Android, profile.Folders));
    }
}
