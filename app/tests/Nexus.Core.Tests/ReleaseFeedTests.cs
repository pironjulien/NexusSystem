using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class ReleaseFeedTests
{
    [Fact]
    public void TheLatestRedirectGivesTheReleaseTag() =>
        Assert.Equal("v0.22.8", ReleaseFeed.TagOf("https://github.com/pironjulien/NexusAndroid/releases/tag/v0.22.8"));

    [Theory]
    [InlineData("https://github.com/pironjulien/NexusAndroid/releases/tag/v0.22.8-dev.12")]
    [InlineData("https://github.com/pironjulien/NexusAndroid/releases")]
    [InlineData(null)]
    public void AnythingElseIsNoRelease(string? location) => Assert.Null(ReleaseFeed.TagOf(location));

    [Theory]
    [InlineData("v0.22.7", "0.22.6", true)]
    [InlineData("v0.22.7", "0.22.7", false)]
    [InlineData("v0.22.6", "0.22.7", false)]
    public void OnlyAHigherVersionIsNewer(string tag, string installed, bool newer) => Assert.Equal(newer, ReleaseFeed.IsNewer(tag, installed));

    [Fact]
    public void EveryProductWithReleasesHasItsRepository()
    {
        foreach (var product in new[] { Products.Windows, Products.Android, Products.Browser, Products.Relay, Products.AgyMail, Products.Keypad })
            Assert.Equal(product.Name, ReleaseFeed.Repository(product));
    }
}
