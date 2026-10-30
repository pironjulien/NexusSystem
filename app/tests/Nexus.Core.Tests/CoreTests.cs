using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

/// <summary>A throwaway user profile, removed after the test.</summary>
public sealed class FakeProfile : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nexus-core-" + Guid.NewGuid().ToString("N"));

    public UserFolders Folders => new(Root, Path.Combine(Root, "AppData", "Roaming"), Path.Combine(Root, "AppData", "Local"));

    public FakeProfile(params string[] folders)
    {
        foreach (var folder in folders.Append(@"AppData\Roaming").Append(@"AppData\Local")) Directory.CreateDirectory(Path.Combine(Root, folder));
    }

    public string Write(string relative, string text)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

public class AiAppsTests
{
    [Fact]
    public void FindsTheThreeApps()
    {
        using var profile = new FakeProfile(".claude", ".codex", @".gemini\antigravity", @"AppData\Roaming\Claude");
        var found = AiApps.Find(profile.Folders);
        Assert.Equal(3, found.Count);
        Assert.Contains(AiApp.ClaudeCode, found);
        Assert.Contains(AiApp.Antigravity, found);
        Assert.Contains(AiApp.Codex, found);
    }

    [Fact]
    public void ClaudeDesktopFromTheStoreReadsClaudeData()
    {
        using var profile = new FakeProfile(@"AppData\Local\Packages\AnthropicPBC.Claude_fnn82j28hfe8t\LocalCache\Roaming\Claude",
            @"AppData\Local\Claude-Data\anthropicpbc.claude_fnn82j28hfe8t");
        profile.Write(@"AppData\Local\Packages\AnthropicPBC.Claude_fnn82j28hfe8t\LocalCache\Roaming\Claude\.user-data-left.anthropicpbc.claude_fnn82j28hfe8t", "");
        var folders = AiApps.DesktopConfigFolders(profile.Folders);
        Assert.Equal([Path.Combine(profile.Root, @"AppData\Local\Claude-Data\anthropicpbc.claude_fnn82j28hfe8t")], folders);
    }

    [Fact]
    public void AFolderClaudeLeftIsNotClaudeDesktop()
    {
        using var profile = new FakeProfile(@"AppData\Roaming\Claude");
        profile.Write(@"AppData\Roaming\Claude\.user-data-left.x", "");
        Assert.Empty(AiApps.DesktopConfigFolders(profile.Folders));
    }
}

public class DeclarationsTests
{
    [Fact]
    public void ReadsEachAppsOwnConfiguration()
    {
        using var profile = new FakeProfile(@"AppData\Roaming\Claude");
        profile.Write(@".claude\settings.json", """{"enabledPlugins":{"nexus-windows@nexus":true,"nexus-android@nexus-plugins":false}}""");
        profile.Write(@"AppData\Roaming\Claude\claude_desktop_config.json", """{"mcpServers":{"nexus-android":{"command":"uvx"}}}""");
        profile.Write(@".codex\config.toml", "model = \"x\"\n\n[mcp_servers.nexus-windows]\ncommand = \"npx\"\n");
        profile.Write(@".gemini\config\plugins\nexus-computer\plugin.json", "{}");
        var f = profile.Folders;
        Environment.SetEnvironmentVariable("CODEX_HOME", null);
        Assert.True(Declarations.IsDeclared(Products.Windows, AiApp.ClaudeCode, f));
        Assert.False(Declarations.IsDeclared(Products.Android, AiApp.ClaudeCode, f));
        Assert.True(Declarations.IsDeclared(Products.Windows, AiApp.Codex, f));
        Assert.False(Declarations.IsDeclared(Products.Android, AiApp.Codex, f));
        Assert.True(Declarations.IsDeclared(Products.Windows, AiApp.Antigravity, f));
        Assert.False(Declarations.IsDeclared(Products.Keypad, AiApp.Antigravity, f));
    }

    [Fact]
    public void AnUnreadableFileIsNotDeclared()
    {
        using var profile = new FakeProfile();
        profile.Write(@".claude\settings.json", "{ not json");
        Assert.False(Declarations.IsDeclared(Products.Windows, AiApp.ClaudeCode, profile.Folders));
    }
}

public class JournalTests
{
    [Fact]
    public void ReadsTheCommonFormatNewestFirst()
    {
        using var profile = new FakeProfile();
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var now = DateTime.UtcNow;
        profile.Write($@".nexus\journal\windows\claude\{month}.jsonl",
            $"{{\"v\":1,\"ts\":\"{now.AddMinutes(-5):o}\",\"agent\":\"claude\",\"app\":\"notepad.exe\",\"action\":\"type\",\"issue\":\"✓\",\"ms\":120,\"premier_plan\":false,\"erreur\":null}}\n" +
            "not json\n" +
            $"{{\"v\":1,\"ts\":\"{now.AddMinutes(-1):o}\",\"agent\":\"claude\",\"app\":\"mspaint.exe\",\"action\":\"click\",\"issue\":\"✗\",\"ms\":80,\"premier_plan\":true,\"erreur\":\"no element\"}}\n");
        profile.Write($@".nexus\journal\windows\codex\{month}.jsonl",
            $"{{\"v\":1,\"ts\":\"{now.AddMinutes(-3):o}\",\"agent\":\"codex\",\"app\":\"explorer.exe\",\"action\":\"look\",\"issue\":\"=\",\"ms\":30,\"premier_plan\":false,\"erreur\":null}}\n");
        Environment.SetEnvironmentVariable("NEXUS_JOURNAL_HOME", null);
        var entries = Journal.Read(Products.Windows, profile.Folders, DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Equal(["mspaint.exe", "explorer.exe", "notepad.exe"], entries.Select(e => e.App));
        Assert.Equal((3, 2, 1, 1), Journal.Summarize(entries));
        Assert.Equal("no element", entries[0].Error);
    }
}

public class EmergencyStopTests
{
    [Fact]
    public void PressWritesANewStopEachTime()
    {
        using var profile = new FakeProfile();
        EmergencyStop.Press(profile.Folders);
        var first = File.ReadAllText(EmergencyStop.StopFile(profile.Folders));
        EmergencyStop.Press(profile.Folders);
        var second = File.ReadAllText(EmergencyStop.StopFile(profile.Folders));
        Assert.NotEqual(first, second);
        Assert.Contains("\"hold_s\":120", second);
        Assert.InRange(EmergencyStop.SecondsLeft(profile.Folders), 110, 120);
    }

    [Fact]
    public void AnOldStopIsOver()
    {
        using var profile = new FakeProfile(".nexus");
        var file = profile.Write(@".nexus\stop", "{}");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(0, EmergencyStop.SecondsLeft(profile.Folders));
    }
}

public class ServersTests
{
    [Fact]
    public void FindsEachServerTreeAndWhoStartedIt()
    {
        var table = new Dictionary<int, (int, string, string)>
        {
            [10] = (1, "Codex.exe", @"C:\Program Files\WindowsApps\OpenAI.Codex\codex.exe"),
            [11] = (10, "node.exe", "node npx-cli.js -y nexus-windows-codex"),
            [12] = (11, "node.exe", @"node C:\x\npm-cache\_npx\nexus-windows-codex\bin\server.js"),
            [20] = (1, "claude.exe", @"C:\Users\x\.local\bin\claude.exe"),
            [21] = (20, "uvx.exe", "uvx --python 3.14 nexus-android==0.11.0"),
            [22] = (21, "python.exe", @"python -m nexus_android"),
            [30] = (1, "python.exe", "python -m something-else"),
        };
        var found = Servers.Match(table).OrderBy(s => s.ProcessId).ToList();
        Assert.Equal(2, found.Count);
        Assert.Equal((Products.Windows, (AiApp?)AiApp.Codex), (found[0].Product, found[0].StartedBy));
        Assert.Equal((Products.Android, (AiApp?)AiApp.ClaudeCode), (found[1].Product, found[1].StartedBy));
    }
}

public class PackTests
{
    [Fact]
    public void ParsesEventsOnly()
    {
        Assert.Null(PackRunner.ParseEvent("== NexusAndroid"));
        var e = PackRunner.ParseEvent("""NEXUS_PACK_EVENT {"ok":true,"type":"cell","product":"android","ai":"codex"}""");
        Assert.NotNull(e);
        Assert.Equal(("cell", "android", "codex", true), (e.Type, e.Text("product"), e.Text("ai"), e.Flag("ok")));
        var inRow = PackRunner.ParseEvent("""  NexusWindows  NEXUS_PACK_EVENT {"ai":"claude-code","ok":false,"type":"cell","product":"windows"}""");
        Assert.Equal(("cell", false), (inRow!.Type, inRow.Flag("ok")));
    }

    [Fact]
    public void TheEmbeddedScriptIsTheCatalogsOwn()
    {
        var script = PackRunner.ExtractScript();
        try
        {
            // From this source file, not the build folder: the build output lives outside the repository.
            var catalog = Path.Combine(SourceDirectory(), @"..\..\..\get-nexus.ps1");
            Assert.True(File.Exists(catalog), "the one-line install is at the repository root");
            Assert.Equal(File.ReadAllBytes(catalog), File.ReadAllBytes(script));
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetDirectoryName(file)!;

    [Fact]
    public async Task DryRunGivesThePlanAsEvents()
    {
        var events = new List<PackEvent>();
        var code = await PackRunner.RunAsync(["windows"], dryRun: true, _ => { }, events.Add, TestContext.Current.CancellationToken);
        Assert.Contains(events, e => e.Type == "done");
        Assert.True(code is 0 or 1);  // 1 when the PC has no AI app
    }
}

public class ProductsTests
{
    [Fact]
    public void SaysWhyAProductDoesNotFit()
    {
        using var profile = new FakeProfile();
        var f = profile.Folders;
        Assert.Equal(Unfit.NeedsTwo, Products.Why(Products.Relay, [AiApp.Codex], f));
        Assert.Null(Products.Why(Products.Relay, [AiApp.ClaudeCode, AiApp.Codex], f));
        Assert.Null(Products.Why(Products.Relay, [AiApp.Codex, AiApp.Antigravity], f));  // Codex hosts (NexusRelay 1.4.0)
        Assert.Equal(Unfit.NoTarget, Products.Why(Products.AgyMail, [AiApp.ClaudeCode, AiApp.Codex], f));
        Assert.Null(Products.Why(Products.AgyMail, [AiApp.Antigravity], f));
        Assert.Equal(Unfit.NeedsDevice, Products.Why(Products.Keypad, AiApps.All, f));
        Directory.CreateDirectory(Path.Combine(f.LocalAppData, "Logi", "LogiPluginService"));
        Assert.Null(Products.Why(Products.Keypad, [], f));
    }

    [Fact]
    public void RelayAndKeypadAreReadWhereTheirInstallersWrite()
    {
        using var profile = new FakeProfile();
        var f = profile.Folders;
        Assert.False(Declarations.IsDeclared(Products.Relay, AiApp.ClaudeCode, f));
        profile.Write(".claude.json", """{"mcpServers":{"nexus-relay":{"type":"http","url":"http://127.0.0.1:16181/mcp"}}}""");
        Assert.True(Declarations.IsDeclared(Products.Relay, AiApp.ClaudeCode, f));
        Assert.False(Declarations.IsDeclared(Products.Windows, AiApp.ClaudeCode, f));  // a catalog plugin, not ~\.claude.json
        // The plugin route failed (no git on a new Windows): NexusAndroid's own installer declared it there.
        profile.Write(".claude.json", """{"mcpServers":{"nexus-android":{"command":"cmd.exe"}}}""");
        Assert.True(Declarations.IsDeclared(Products.Android, AiApp.ClaudeCode, f));
        Assert.False(Declarations.DeviceInstalled(Products.Keypad, f));
        Directory.CreateDirectory(Path.Combine(f.LocalAppData, "Logi", "LogiPluginService", "Plugins", "NexusKeypad"));
        Assert.True(Declarations.DeviceInstalled(Products.Keypad, f));
    }

    [Theory]
    [InlineData("TrinitePad")]
    [InlineData("AiAgentsPc")]
    [InlineData("PcDashboard")]
    public void KeypadMarketplaceEditions(string edition)
    {
        using var profile = new FakeProfile();
        Directory.CreateDirectory(Path.Combine(profile.Folders.LocalAppData, "Logi", "LogiPluginService", "Plugins", edition));
        Assert.True(Declarations.DeviceInstalled(Products.Keypad, profile.Folders));
    }

    [Fact]
    public void KeypadStateFile()
    {
        using var profile = new FakeProfile();
        profile.Write(@".nexus\status\keypad.json", """{"product":"keypad","version":"1.0.0","state":"idle"}""");
        Assert.True(Declarations.DeviceInstalled(Products.Keypad, profile.Folders));
    }
}

public class DesktopExtensionTests
{
    [Fact]
    public void InstalledWhenClaudeExtensionsHoldsItsManifest()
    {
        using var profile = new FakeProfile(@"AppData\Roaming\Claude");
        var browser = DesktopExtension.For(Products.Browser)!;
        Assert.False(browser.IsInstalled(profile.Folders));
        Assert.False(Declarations.IsDeclared(Products.Browser, AiApp.ClaudeCode, profile.Folders));
        profile.Write(@"AppData\Roaming\Claude\Claude Extensions\local.mcpb.julien-piron.nexus-browser\manifest.json", """{"name": "nexus-browser"}""");
        Assert.True(browser.IsInstalled(profile.Folders));
        // A Claude Desktop extension never makes Claude Code look ready: Nexus lives in Code.
        Assert.False(Declarations.IsDeclared(Products.Browser, AiApp.ClaudeCode, profile.Folders));
        Assert.False(DesktopExtension.For(Products.Android)!.IsInstalled(profile.Folders));
    }

    [Fact]
    public void OnlyProductsWithAnExtension()
    {
        Assert.True(DesktopExtension.For(Products.Windows)!.Versioned);
        Assert.Null(DesktopExtension.For(Products.Relay));
        Assert.Null(DesktopExtension.For(Products.Keypad));
        Assert.Null(DesktopExtension.For(Products.AgyMail));
    }
}

[Collection("bridge")]
public class BrowserBridgeTests
{
    [Fact]
    public async Task NotInstalledWithoutToken()
    {
        using var profile = new FakeProfile();
        using var http = new HttpClient();
        var saved = Environment.GetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE");
        Environment.SetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE", null);
        try { Assert.Equal(BridgeState.NotInstalled, await BrowserBridge.StateAsync(profile.Folders, http, TestContext.Current.CancellationToken)); }
        finally { Environment.SetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE", saved); }
    }

    [Fact]
    public void ExtensionIdFromTheInstalledConfiguration()
    {
        using var profile = new FakeProfile();
        Assert.Null(BrowserBridge.ExtensionId(profile.Folders));
        const string path = @"AppData\Local\NexusBrowser\app\native-host\bridge_config.json";
        profile.Write(path, """{"allowed_origins": ["chrome-extension://blcgddmjdkobmgakjffjpgiicjlflgjf/"], "store_id": "fjapmaccbgdfneoccclmlpgknbbfkaco", "store_public": false}""");
        Assert.Equal("blcgddmjdkobmgakjffjpgiicjlflgjf", BrowserBridge.ExtensionId(profile.Folders));
        profile.Write(path, """{"allowed_origins": ["chrome-extension://blcgddmjdkobmgakjffjpgiicjlflgjf/"], "store_id": "fjapmaccbgdfneoccclmlpgknbbfkaco", "store_public": true}""");
        Assert.Equal("fjapmaccbgdfneoccclmlpgknbbfkaco", BrowserBridge.ExtensionId(profile.Folders));
    }

    [Fact]
    public void UnpackagedTestRun() => Assert.False(Packaging.IsPackaged);
}

[Collection("bridge")]
public class BrowserBridgeApiTests
{
    // A stand-in bridge on localhost: answers /api/status and /api/pair as NexusBrowser 2.25.0 does (or 404 for older).
    static async Task<T> WithBridge<T>(bool pairing, int clients, Func<FakeProfile, Task<T>> test, bool? chromeRunning = true)
    {
        var port = 20000 + Random.Shared.Next(20000);
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var serve = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch (System.Net.HttpListenerException) { return; } catch (ObjectDisposedException) { return; }
                var authorized = context.Request.Headers["X-Nexus-Browser-Token"] == "secret-token";
                var (code, body) = (context.Request.Url!.AbsolutePath, authorized) switch
                {
                    (_, false) => (401, "{}"),
                    ("/api/status", _) => (200, $"{{\"status\":\"online\",\"connected_clients\":{clients}" +
                        (chromeRunning is { } running ? $",\"chrome_running\":{running.ToString().ToLowerInvariant()}" : "") + "}"),
                    ("/api/pair", _) when pairing && context.Request.HttpMethod == "POST" => (200, "{\"secret\":\"abc123\",\"expires_in_s\":300}"),
                    ("/api/update", _) when context.Request.HttpMethod == "POST" => (pairing ? 202 : 409, "{}"),
                    _ => (404, "{}"),
                };
                context.Response.StatusCode = code;
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        using var profile = new FakeProfile();
        var token = profile.Write(@".nexus\nexus-browser\bridge-token", "secret-token\n");
        var saved = (BrowserBridge.Api, Environment.GetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE"));
        BrowserBridge.Api = $"http://localhost:{port}";
        Environment.SetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE", token);
        try { return await test(profile); }
        finally
        {
            (BrowserBridge.Api, var file) = saved;
            Environment.SetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE", file);
            listener.Stop();
        }
    }

    [Fact]
    public async Task WaitingThenOffer()
    {
        using var http = new HttpClient();
        Assert.Equal(BridgeState.Waiting, await WithBridge(true, 0, p => BrowserBridge.StateAsync(p.Folders, http)));
        Assert.Equal(BridgeState.Connected, await WithBridge(true, 2, p => BrowserBridge.StateAsync(p.Folders, http)));
        Assert.Equal("abc123", await WithBridge(true, 0, p => BrowserBridge.OfferAsync(p.Folders, http)));
    }

    [Fact]
    public async Task ClosedChromeIsIdleRatherThanAnExtensionFailure()
    {
        using var http = new HttpClient();
        Assert.Equal(BridgeState.BrowserClosed, await WithBridge(true, 0, p => BrowserBridge.StateAsync(p.Folders, http), false));
        Assert.Equal(BridgeState.Connected, await WithBridge(true, 1, p => BrowserBridge.StateAsync(p.Folders, http), false));
        var legacy = await WithBridge(true, 0, p => BrowserBridge.StateAsync(p.Folders, http), null);
        Assert.Contains(legacy, new[] { BridgeState.Waiting, BridgeState.BrowserClosed });
    }

    [Fact]
    public async Task BridgeTakesTheUpdateRequest()
    {
        using var http = new HttpClient();
        Assert.Equal(UpdateRequest.Started, await WithBridge(true, 0, _ => ProductUpdates.RequestAsync(Products.Browser, http)));
        Assert.Equal(UpdateRequest.NotSelfUpdating, await WithBridge(false, 0, _ => ProductUpdates.RequestAsync(Products.Browser, http)));
    }

    [Fact]
    public async Task OlderBridgeHasNoPairing()
    {
        using var http = new HttpClient();
        Assert.Null(await WithBridge(false, 0, p => BrowserBridge.OfferAsync(p.Folders, http)));
    }
}

[Collection("bridge")]
public class ProductUpdatesTests
{
    [Theory]
    [InlineData("3.3.0", "3.2.8", false)]   // running ahead of the last daily check (27/10: NexusWindows for Antigravity)
    [InlineData("3.3.0", "3.3.0", false)]
    [InlineData("3.2.8", "3.3.0", true)]
    [InlineData("3.10.0", "3.9.1", false)]  // numeric, not text order
    [InlineData("1.6.0", "1.6.0-beta", false)]
    public void OnlyANewerPublishedVersionIsAnUpdate(string version, string latest, bool available) =>
        Assert.Equal(available, new ProductState("p", version, latest, "idle", null, null).UpdateAvailable);

    [Fact]
    public void ReadsEachEditionState()
    {
        using var profile = new FakeProfile();
        Assert.Empty(ProductUpdates.Read(Products.Windows, profile.Folders));
        profile.Write(@".nexus\status\windows-codex.json", """{"product":"windows-codex","version":"3.1.6","latest":"3.1.7","state":"idle","checked_at":"2026-10-26T10:00:00Z","message":"m"}""");
        profile.Write(@".nexus\status\windows-claude.json", """{"product":"windows-claude","version":"3.3.0","latest":"3.3.0","state":"idle"}""");
        profile.Write(@".nexus\status\windows-antigravity.json", "{ being written");
        var states = ProductUpdates.Read(Products.Windows, profile.Folders);
        Assert.Equal(["windows-claude", "windows-codex"], states.Select(s => s.Name));
        Assert.False(states[0].UpdateAvailable);
        Assert.True(states[1].UpdateAvailable);
        Assert.Equal("m", states[1].Message);
        Assert.True(ProductUpdates.IsInstalled(Products.Windows, [], profile.Folders));
        Assert.False(ProductUpdates.IsInstalled(Products.Android, [], profile.Folders));
    }

    [Fact]
    public void OnlyRelayAndKeypadCompleteAreAsked()
    {
        using var profile = new FakeProfile();
        Assert.False(ProductUpdates.CanRequest(Products.Windows, profile.Folders));
        Assert.False(ProductUpdates.CanRequest(Products.Android, profile.Folders));
        Assert.False(ProductUpdates.CanRequest(Products.Keypad, profile.Folders));
        Directory.CreateDirectory(Path.Combine(profile.Folders.LocalAppData, "Logi", "LogiPluginService", "Plugins", "NexusKeypad"));
        Assert.True(ProductUpdates.CanRequest(Products.Keypad, profile.Folders));
    }

    [Theory]
    [InlineData(202, UpdateRequest.Started)]
    [InlineData(200, UpdateRequest.UpToDate)]
    [InlineData(409, UpdateRequest.NotSelfUpdating)]
    [InlineData(401, UpdateRequest.Unreachable)]
    public async Task RelayDaemonAnswers(int code, UpdateRequest expected)
    {
        var port = 20000 + Random.Shared.Next(20000);
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var ok = context.Request.HttpMethod == "POST" && context.Request.Url!.AbsolutePath == "/api/update" &&
                context.Request.Headers["Authorization"] == "Bearer relay-token";
            context.Response.StatusCode = ok ? code : 400;
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        var saved = (ProductUpdates.RelayApi, Environment.GetEnvironmentVariable("NEXUS_MCP_TOKEN"));
        ProductUpdates.RelayApi = $"http://localhost:{port}";
        Environment.SetEnvironmentVariable("NEXUS_MCP_TOKEN", "relay-token");
        try
        {
            using var http = new HttpClient();
            Assert.Equal(expected, await ProductUpdates.RequestAsync(Products.Relay, http, TestContext.Current.CancellationToken));
        }
        finally
        {
            (ProductUpdates.RelayApi, var token) = saved;
            Environment.SetEnvironmentVariable("NEXUS_MCP_TOKEN", token);
            listener.Stop();
        }
        await serve;
    }

    [Fact]
    public async Task KeypadPipeGetsOnlyUpdate()
    {
        var name = "nexus-test-" + Guid.NewGuid().ToString("N");
        string? received = null;
        var serve = Task.Run(async () =>
        {
            await using var pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            received = await reader.ReadLineAsync();
            await writer.WriteLineAsync("started");
        }, TestContext.Current.CancellationToken);
        var saved = ProductUpdates.KeypadPipe;
        ProductUpdates.KeypadPipe = name;
        try
        {
            using var http = new HttpClient();
            Assert.Equal(UpdateRequest.Started, await ProductUpdates.RequestAsync(Products.Keypad, http, TestContext.Current.CancellationToken));
            await serve;
            Assert.Equal("update", received);
        }
        finally { ProductUpdates.KeypadPipe = saved; }
    }

    [Fact]
    public async Task KeypadPipeAbsent()
    {
        var saved = ProductUpdates.KeypadPipe;
        ProductUpdates.KeypadPipe = "nexus-test-absent-" + Guid.NewGuid().ToString("N");
        try
        {
            using var http = new HttpClient();
            Assert.Equal(UpdateRequest.Unreachable, await ProductUpdates.RequestAsync(Products.Keypad, http, TestContext.Current.CancellationToken));
        }
        finally { ProductUpdates.KeypadPipe = saved; }
    }
}

[Collection("bridge")]
public class DeclarationStateTests
{
    [Fact]
    public void PublicCodexPluginsAreRecognizedWithoutLegacyServerTables()
    {
        using var profile = new FakeProfile(".codex");
        profile.Write(@".codex\config.toml", string.Join("\n",
            @"[plugins.""nexus-windows@nexus-plugins""]", "enabled = true", "",
            @"[plugins.""nexus-android@nexus-plugins""]", "enabled = true", "",
            @"[plugins.""nexus-browser@nexus-plugins""]", "enabled = false", "",
            @"[plugins.""nexus-relay@nexus-plugins""]", "enabled = true"));
        var saved = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(profile.Root, ".codex"));
        try
        {
            Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Windows, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Android, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Disabled, Declarations.State(Products.Browser, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Relay, AiApp.Codex, profile.Folders));
        }
        finally { Environment.SetEnvironmentVariable("CODEX_HOME", saved); }
    }

    [Fact]
    public void CodexRealState()
    {
        using var profile = new FakeProfile(".codex");
        profile.Write(@".codex\config.toml", string.Join("\n",
            "[mcp_servers.nexus-windows]", @"command = ""C:\nowhere\node.exe""", "",
            "[mcp_servers.nexus-android]", @"command = ""powershell.exe""", "[mcp_servers.nexus-android.env]", @"X = ""1""", "",
            "[mcp_servers.nexus-relay]", @"url = ""http://127.0.0.1:16181/mcp""", "",
            @"[plugins.""nexus-browser@nexus-plugins""]", "enabled = false"));
        var saved = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(profile.Root, ".codex"));
        try
        {
            Assert.Equal(DeclarationState.Invalid, Declarations.State(Products.Windows, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Android, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Relay, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Disabled, Declarations.State(Products.Browser, AiApp.Codex, profile.Folders));
            Assert.Equal(DeclarationState.Missing, Declarations.State(Products.AgyMail, AiApp.Codex, profile.Folders));
        }
        finally { Environment.SetEnvironmentVariable("CODEX_HOME", saved); }
    }

    [Fact]
    public void AntigravityPluginTurnedOff()
    {
        using var profile = new FakeProfile();
        foreach (var plugin in new[] { "nexus-computer", "nexus-android" }) profile.Write($@".gemini\config\plugins\{plugin}\plugin.json", "{}");
        profile.Write(@".gemini\config\config.json", """{"plugins": {"nexus-computer": {"enabled": false}, "nexus-android": {"enabled": true}}}""");
        Assert.Equal(DeclarationState.Disabled, Declarations.State(Products.Windows, AiApp.Antigravity, profile.Folders));
        Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Android, AiApp.Antigravity, profile.Folders));
        Assert.Equal(DeclarationState.Missing, Declarations.State(Products.Browser, AiApp.Antigravity, profile.Folders));
    }

    [Fact]
    public void ClaudeCodePluginTurnedOff()
    {
        using var profile = new FakeProfile(".claude");
        profile.Write(@".claude\settings.json", """{"enabledPlugins": {"nexus-windows@nexus-plugins": false, "nexus-android@nexus-plugins": true}}""");
        Assert.Equal(DeclarationState.Disabled, Declarations.State(Products.Windows, AiApp.ClaudeCode, profile.Folders));
        Assert.Equal(DeclarationState.Declared, Declarations.State(Products.Android, AiApp.ClaudeCode, profile.Folders));
        Assert.Equal(DeclarationState.Missing, Declarations.State(Products.Browser, AiApp.ClaudeCode, profile.Folders));
    }
}

public class ErrorLogTests
{
    [Fact]
    public void Lines_are_timestamped_and_the_file_is_capped()
    {
        using var profile = new FakeProfile();
        var log = new ErrorLog(Path.Combine(profile.Root, "NexusSystem", "app.log"));
        log.Error("step", new InvalidOperationException("boom"));
        var line = File.ReadAllText(log.File);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}.*step: System.InvalidOperationException: boom", line);

        File.WriteAllText(log.File, new string('x', (int)ErrorLog.MaxBytes + 1));
        log.Info("start 1.0");
        Assert.True(File.Exists(log.File + ".1"));
        Assert.EndsWith("start 1.0" + Environment.NewLine, File.ReadAllText(log.File));
    }

    [Fact]
    public void A_log_that_cannot_be_written_never_throws()
    {
        using var profile = new FakeProfile();
        var blocked = profile.Write("blocked", "a file, not a folder");
        new ErrorLog(Path.Combine(blocked, "app.log")).Info("ignored");
    }

    [Fact]
    public void AiPrompt_matches_the_selected_products_in_canonical_order()
    {
        Assert.Equal("", Products.AiPrompt([]));
        var fr = Products.AiPrompt(["browser", "windows"], french: true);
        Assert.Contains("NexusWindows, NexusBrowser", fr);
        Assert.Contains("-Only windows,browser -NoGuide", fr);
        Assert.Contains("INSTALL-AI.md", fr);

        var en = Products.AiPrompt(["keypad"], french: false);
        Assert.Contains("Install and set up NexusKeypad", en);
        Assert.Contains("-Only keypad -NoGuide", en);
    }
}
