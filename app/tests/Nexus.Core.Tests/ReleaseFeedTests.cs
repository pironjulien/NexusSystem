using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class ReleaseFeedTests
{
    const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry><id>x</id><link rel="alternate" type="text/html" href="https://github.com/pironjulien/NexusAndroid/releases/tag/v0.22.7-dev.9"/></entry>
          <entry><id>y</id><link rel="alternate" type="text/html" href="https://github.com/pironjulien/NexusAndroid/releases/tag/v0.22.6"/></entry>
        </feed>
        """;

    [Fact]
    public void TheNewestFeedEntryIsTheDevelopmentChannel() => Assert.Equal("v0.22.7-dev.9", ReleaseFeed.NewestInFeed(Feed));

    [Fact]
    public void OtherTagsInTheFeedAreIgnored() => Assert.Equal("v0.22.6", ReleaseFeed.NewestInFeed(Feed.Replace("v0.22.7-dev.9", "nightly")));

    [Fact]
    public void ChannelIsStableUnlessThisPcChoseDev()
    {
        using var profile = new FakeProfile(".nexus");
        Assert.Equal(ReleaseFeed.Stable, ReleaseFeed.Channel(profile.Folders));
        profile.Write(@".nexus\nexussystem.json", """{"skip":["claude-desktop"],"channel":"dev"}""");
        Assert.Equal(ReleaseFeed.Development, ReleaseFeed.Channel(profile.Folders));
        profile.Write(@".nexus\nexussystem.json", """{"channel":"nightly"}""");
        Assert.Equal(ReleaseFeed.Stable, ReleaseFeed.Channel(profile.Folders));
    }

    [Theory]
    [InlineData("v0.22.7-dev.9", "0.22.6", true)]
    [InlineData("v0.22.7-dev.9", "0.22.7", false)]  // a development build reports the version it will be promoted as
    [InlineData("v0.22.6", "0.22.7", false)]
    public void OnlyAHigherVersionIsNewer(string tag, string installed, bool newer) => Assert.Equal(newer, ReleaseFeed.IsNewer(tag, installed));

    [Fact]
    public void EveryProductWithReleasesHasItsRepository()
    {
        foreach (var product in new[] { Products.Windows, Products.Android, Products.Browser, Products.Relay, Products.AgyMail, Products.Keypad })
            Assert.Equal(product.Name, ReleaseFeed.Repository(product));
    }
}
