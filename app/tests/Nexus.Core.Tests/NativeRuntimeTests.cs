using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class NativeRuntimeTests
{
    [Fact]
    public void NativeServersAreFoundWithoutCountingChromeAuthenticationOrHelpers()
    {
        var table = new Dictionary<int, (int, string, string)>
        {
            [1] = (0, "Codex.exe", "Codex.exe"),
            [2] = (0, "Antigravity.exe", "Antigravity.exe"),
            [10] = (0, "NexusAndroid.exe", @"C:\Local\NexusAndroid\native\NexusAndroid.exe --http 16180"),
            [11] = (0, "NexusRelay.exe", @"C:\Local\NexusRelay\native\NexusRelay.exe --http 16181"),
            [12] = (0, "NexusBrowser.exe", @"C:\Local\NexusBrowser\native\NexusBrowser.exe --bridge"),
            [13] = (1, "NexusBrowser.exe", @"C:\Local\NexusBrowser\native\NexusBrowser.exe --mcp"),
            [14] = (2, "NexusAGYmail.exe", @"C:\Local\NexusAGYmail\native\NexusAGYmail.exe --mcp"),
            [15] = (2, "NexusCodex.UIA.exe", @"C:\Local\NexusCodex\ComputerUse\native\NexusCodex.UIA.exe --mcp --agent antigravity"),
            [20] = (0, "NexusBrowser.exe", "NexusBrowser.exe chrome-extension://abcdefghijklmnopabcdefghijklmnop/ --parent-window=0"),
            [21] = (0, "NexusBrowser.exe", "NexusBrowser.exe --selftest"),
            [22] = (1, "NexusCodex.UIA.exe", "NexusCodex.UIA.exe --serve"),
            [23] = (0, "NexusAndroid.exe", "NexusAndroid.exe --version"),
        };
        var found = Servers.Match(table).ToDictionary(s => s.ProcessId);
        Assert.Equal(6, found.Count);
        Assert.Equal(Products.Android, found[10].Product);
        Assert.Equal(Products.Relay, found[11].Product);
        Assert.Null(found[12].StartedBy);
        Assert.Equal(AiApp.Codex, found[13].StartedBy);
        Assert.Equal(Products.AgyMail, found[14].Product);
        Assert.Equal(AiApp.Antigravity, found[15].StartedBy);
    }

    [Fact]
    public void NativeChildIsNotCountedTwiceWhenStartedThroughLegacyLauncher()
    {
        var table = new Dictionary<int, (int, string, string)>
        {
            [1] = (0, "Codex.exe", "Codex.exe"),
            [2] = (1, "cmd.exe", @"cmd /c nexus-browser --mcp"),
            [3] = (2, "NexusBrowser.exe", @"C:\Local\NexusBrowser\native\NexusBrowser.exe --mcp"),
        };
        var server = Assert.Single(Servers.Match(table));
        Assert.Equal(2, server.ProcessId);
        Assert.Equal(AiApp.Codex, server.StartedBy);
    }

    [Fact]
    public void AStoredVersionIsNotARecentRuntimeCheck()
    {
        var now = DateTimeOffset.Parse("2026-10-29T21:00:00Z");
        var state = new ProductState("browser", "2.31.6", "2.31.6", "idle", now.AddHours(-25), null);
        Assert.False(state.IsFresh(now));
        Assert.False((state with { CheckedAt = null }).IsFresh(now));
        Assert.False((state with { CheckedAt = now.AddHours(1) }).IsFresh(now));
        Assert.True((state with { CheckedAt = now.AddHours(-1) }).IsFresh(now));
    }

    [Fact]
    public void IncompleteNativeStatusDoesNotDefaultToIdle()
    {
        using var profile = new FakeProfile();
        profile.Write(@".nexus\status\windows-antigravity.json", """{"version":"2.1.0","updatedAt":"2026-10-29T21:00:00Z"}""");
        var state = Assert.Single(ProductUpdates.Read(Products.Windows, profile.Folders));
        Assert.Equal("unknown", state.State);
        Assert.Equal(DateTimeOffset.Parse("2026-10-29T21:00:00Z"), state.CheckedAt);
    }
}
