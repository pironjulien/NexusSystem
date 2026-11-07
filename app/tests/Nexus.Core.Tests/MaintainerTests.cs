using System.Security.Cryptography;
using System.Text.Json;
using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

/// <summary>
/// The hourly maintenance engine's real decisions on a throwaway profile: releases, installers, plugins and running
/// processes are stand-ins, so no network request, installer, AI process or real configuration is ever touched.
/// </summary>
sealed class FakeServices(UserFolders folders) : MaintenanceServices(folders)
{
    public string Version = "1.6.36";
    public string OwnSha = new('a', 64);
    public string EngineTag = "v1.6.36";
    public string EngineSha = new('a', 64);
    public Dictionary<string, string> Tags = new() { ["NexusSystem"] = "v1.6.35", ["NexusWindows"] = "v3.9.5", ["NexusBrowser"] = "v2.33.5" };
    public Dictionary<string, string?> Installed = new() { ["windows"] = "3.9.4" };
    public Dictionary<string, string> Sources = new() { ["windows"] = "release", ["browser"] = "release" };
    public IReadOnlyList<string> Found = ["codex", "claude-code"];
    public string? PluginVersion = "3.9.5";
    public string Loaded = "3.9.4";
    public bool NetworkFailure, InstallerFailure, InstallerThrows;
    public int Installs, PluginRefresh, EngineRuns;
    public List<string> Downloads = [];
    public List<string> RunInstallerTargets = [];
    public string? InstalledEngine;
    public string? KeptCatalogue;

    public override string ManagerVersion => Version;
    public override string ManagerSha256() => OwnSha;
    public override string? Token() => "fixture credential";
    public override IReadOnlyList<string> FindAiApps() => Found;
    public override string? InstalledVersion(MaintenanceProduct product) => Installed.GetValueOrDefault(product.Key);
    public override string InstallationSource(MaintenanceProduct product) => Sources.GetValueOrDefault(product.Key, "unknown");

    public override Task<string> LatestTagAsync(string repository, CancellationToken cancel) =>
        NetworkFailure ? throw new HttpRequestException("network unavailable") : Task.FromResult(Tags[repository]);

    public override Task<PublishedFile> PublishedInstallerAsync(string repository, string work, CancellationToken cancel)
    {
        Downloads.Add(repository);
        if (NetworkFailure) throw new HttpRequestException("network unavailable");
        var tag = Tags[repository];
        return Task.FromResult(new PublishedFile(tag, tag[1..], Path.Combine(work, repository, "get.ps1"), new string('b', 64)));
    }

    public override Task<PublishedFile> PublishedEngineAsync(CancellationToken cancel)
    {
        Downloads.Add("NexusSystem");
        return Task.FromResult(new PublishedFile(EngineTag, EngineTag[1..], PublishedEngineName, EngineSha));
    }

    public override Task<string> InstallPublishedEngineAsync(PublishedFile published, string work, CancellationToken cancel)
    {
        InstalledEngine = MaintenanceUpdates.ExecutablePath(Folders);
        return Task.FromResult(InstalledEngine);
    }

    public override Task<int> RunEngineAsync(string engine, CancellationToken cancel)
    {
        EngineRuns++;
        return Task.FromResult(7);
    }

    public override void CheckInstallerContract(string file, IReadOnlyList<string> parameters) =>
        Assert.Contains("UpdateOnly", parameters);

    public override Task<int> RunInstallerAsync(string what, string file, IReadOnlyList<string> arguments, string log, string? targets, CancellationToken cancel)
    {
        Assert.Equal("-UpdateOnly", string.Join(',', arguments));
        File.WriteAllText(log, "fixture installer output\nError: fixture cause\n");
        RunInstallerTargets.Add(targets ?? "");
        if (InstallerThrows) throw new InvalidOperationException("fixture installer exception");
        Installs++;
        if (InstallerFailure) return Task.FromResult(1);
        Installed["windows"] = "3.9.5";
        return Task.FromResult(0);
    }

    public override Task MaintainPluginsAsync(MaintenanceProduct product, IReadOnlyList<MaintenanceRoute> routes, CancellationToken cancel)
    {
        PluginRefresh++;
        var route = Assert.Single(routes);
        Assert.Equal("codex", route.Key);
        route.PluginVersion = PluginVersion;
        if (KeptCatalogue is not null) (route.State, route.Reason) = ("kept", KeptCatalogue);
        return Task.CompletedTask;
    }

    public override LoadedEngines LoadedVersions(MaintenanceProduct product) =>
        new([Loaded], false, [new LoadedConnection("codex", Loaded, null)]);
}

[Collection("bridge")]
public class MaintainerTests
{
    static MaintenanceProduct Windows => MaintenanceProduct.All.Single(p => p.Key == "windows");
    static MaintenanceProduct Browser => MaintenanceProduct.All.Single(p => p.Key == "browser");

    static JsonElement Status(FakeProfile profile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(MaintenanceUpdates.StatusPath(profile.Folders)));
        return document.RootElement.Clone();
    }

    static JsonElement Row(FakeProfile profile, int index = 0) => Status(profile).GetProperty("products")[index];

    static FakeProfile CodexProfile(string config)
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", null);
        var profile = new FakeProfile(".codex");
        profile.Write(@".codex\config.toml", config);
        return profile;
    }

    [Fact]
    public void RoutesPreserveDisabledCustomAndOtherCatalogues()
    {
        var maintainer = (FakeProfile p) => new Maintainer(new FakeServices(p.Folders));
        using (var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=false"))
        {
            var before = File.ReadAllText(profile.Folders.CodexConfig);
            Assert.Equal("disabled", maintainer(profile).Profile(Windows, "codex").State);
            Assert.Equal(before, File.ReadAllText(profile.Folders.CodexConfig));
        }
        using (var profile = CodexProfile("[mcp_servers.nexus-windows]\ncommand='powershell.exe'\nargs=['custom.ps1']"))
            Assert.Equal("custom", maintainer(profile).Profile(Windows, "codex").Route);
        using (var profile = CodexProfile("[plugins.\"nexus-windows@personal\"]\nenabled=true"))
            Assert.Equal("kept", maintainer(profile).Profile(Windows, "codex").State);
        using (var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true"))
            Assert.Equal("catalogue", maintainer(profile).Profile(Windows, "codex").Route);
        using var folders = new FakeProfile();
        static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
        Assert.False(Maintainer.OwnedJsonRoute("browser", Json("""{"url":"http://127.0.0.1:16182/mcp?agent=codex&custom=1"}"""), "codex", folders.Folders, null));
        var stable = Path.Combine(folders.Root, ".nexus", "nexus-windows", "NexusWindows.exe").Replace(@"\", @"\\", StringComparison.Ordinal);
        Assert.False(Maintainer.OwnedJsonRoute("windows", Json($$$"""{"command":"{{{stable}}}","args":["--mcp","--agent","codex"],"env":{"CUSTOM":"yes"}}"""), "codex", folders.Folders, null));
        Assert.True(Maintainer.OwnedJsonRoute("windows", Json($$$"""{"command":"{{{stable}}}","args":["--mcp","--agent","codex"]}"""), "codex", folders.Folders, null));
        Assert.True(Maintainer.OwnedJsonRoute("browser", Json("""{"serverUrl":"http://127.0.0.1:16182/mcp?agent=antigravity","headers":{"Authorization":"Bearer fixture credential"}}"""),
            "antigravity", folders.Folders, "fixture credential"));
        Assert.True(Maintainer.OwnedJsonRoute("relay", Json("""{"type":"http","url":"http://127.0.0.1:16181/mcp?agent=codex"}"""), "codex", folders.Folders, null));
        Assert.False(Maintainer.OwnedJsonRoute("relay", Json("""{"type":"http","url":"http://127.0.0.1:16181/mcp?agent=codex","disabled":true}"""), "codex", folders.Folders, null));
    }

    [Fact]
    public void ReceiptRoundTripsThroughTheAppsReader()
    {
        using var profile = new FakeProfile();
        var services = new FakeServices(profile.Folders);
        var receipt = new MaintenanceReceipt { State = "pending", Latest = "1.6.36", Catalogue = "v1.6.36", ActivationPending = true };
        receipt.Products.Add(new MaintenanceRow
        {
            Key = "windows", PublishedVersion = "3.9.5", InstalledVersion = "3.9.5", LoadedVersions = ["3.9.4"], State = "pending",
            Profiles = [new MaintenanceRoute { Key = "codex", State = "pending", Route = "catalogue", PluginVersion = "3.9.5", Observed = true, ActivationPending = true }],
        });
        services.WriteStatus(receipt);
        var read = MaintenanceUpdates.Read(profile.Folders)!;
        Assert.Equal("pending", read.State);
        Assert.True(read.IsFresh(DateTimeOffset.UtcNow));
        var row = Assert.Single(read.Products);
        Assert.Equal(("3.9.5", "3.9.5", "3.9.4"), (row.PublishedVersion, row.InstalledVersion, Assert.Single(row.LoadedVersions)));
        Assert.True(Assert.Single(row.Profiles).ActivationPending);
        var json = Row(profile).GetProperty("profiles")[0];
        Assert.Equal(JsonValueKind.Null, json.GetProperty("guideLoaded").ValueKind);
    }

    [Fact]
    public void InstalledVersionsRequireTheInstallersBytes()
    {
        using var profile = new FakeProfile();
        var services = new MaintenanceServices(profile.Folders);
        var native = Path.Combine(profile.Root, ".nexus", "nexus-windows", "native", new string('a', 64));
        var artifacts = new Dictionary<string, string>();
        foreach (var name in new[] { "NexusWindows.exe", "NexusWindows.dll", "NexusWindows.deps.json", "NexusWindows.runtimeconfig.json" })
            artifacts[name] = MaintenanceServices.Sha256(profile.Write(Path.Combine(native, name), "fixture " + name));
        profile.Write(@".nexus\nexus-windows\installation.json", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Schema"] = 2, ["Version"] = "3.9.4", ["SourceRoot"] = "release v3.9.4", ["NativeHash"] = new string('a', 64),
            ["NativeExecutable"] = Path.Combine(native, "NexusWindows.exe"), ["NativeArtifacts"] = artifacts,
        }));
        Assert.Equal("3.9.4", services.InstalledVersion(Windows));
        Assert.Equal("release", services.InstallationSource(Windows));
        File.WriteAllText(Path.Combine(native, "NexusWindows.dll"), "modified");
        Assert.Null(services.InstalledVersion(Windows));

        var fingerprint = Convert.ToHexStringLower(SHA256.HashData("browser engine"u8));
        var version = Path.Combine(profile.Root, @"AppData\Local\NexusBrowser\versions", fingerprint[..16]);
        var engine = profile.Write(Path.Combine(version, "NexusBrowser.exe"), "browser engine");
        profile.Write(@"AppData\Local\NexusBrowser\current.json", JsonSerializer.Serialize(new { version = "2.33.4", exe = engine, fingerprint }));
        Assert.Equal("2.33.4", services.InstalledVersion(Browser));
        profile.Write(Path.Combine(version, "installation.json"), JsonSerializer.Serialize(new { repository_root = Path.Combine(profile.Root, "moved", "NexusBrowser") }));
        Assert.Equal("repository", services.InstallationSource(Browser));
        var published = Path.Combine(profile.Root, @"AppData\Local\NexusBrowser\app");
        profile.Write(Path.Combine(version, "installation.json"), JsonSerializer.Serialize(new { repository_root = published }));
        Assert.Equal("release", services.InstallationSource(Browser));
        profile.Write(Path.Combine(published, ".git"), "gitdir: a checkout marker");
        Assert.Equal("repository", services.InstallationSource(Browser));
        File.Delete(Path.Combine(version, "installation.json"));
        Assert.Equal("unknown", services.InstallationSource(Browser));

        var keypad = profile.Write(@"AppData\Local\Logi\LogiPluginService\Plugins\NexusKeypad\metadata\LoupedeckPackage.yaml", "name: NexusKeypad\nversion: 1.8.47\n");
        Assert.Equal("1.8.47", services.InstalledVersion(MaintenanceProduct.All.Single(p => p.Key == "keypad")));
        File.Delete(keypad);
        Assert.Null(services.InstalledVersion(MaintenanceProduct.All.Single(p => p.Key == "keypad")));
    }

    [Fact]
    public void LoadedRuntimeNeedsRealHostBirthAndAssemblyBytes()
    {
        using var profile = new FakeProfile();
        var native = Path.Combine(profile.Root, ".nexus", "nexus-windows", "native", new string('a', 64));
        var engine = profile.Write(Path.Combine(native, "NexusWindows.exe"), "engine");
        var assembly = profile.Write(Path.Combine(native, "NexusWindows.dll"), "assembly");
        var digest = MaintenanceServices.Sha256(assembly);
        profile.Write(@".nexus\nexus-windows\installation.json", JsonSerializer.Serialize(new
        {
            NativeExecutable = engine, NativeArtifacts = new Dictionary<string, string>
            {
                ["NexusWindows.exe"] = MaintenanceServices.Sha256(engine), ["NexusWindows.dll"] = digest,
                ["NexusWindows.deps.json"] = new string('d', 64), ["NexusWindows.runtimeconfig.json"] = new string('e', 64),
            },
        }));
        var birth = DateTime.UtcNow;
        profile.Write($@".nexus\status\runtime\windows-codex-800-{birth.Ticks}-session.json", JsonSerializer.Serialize(new
        {
            schema = 1, product = "windows", pid = 800, agent = "codex", state = "idle", processStartUtc = birth.ToString("O"),
            processStartTicks = birth.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), executable = engine, assembly, assemblySha256 = digest, version = "3.9.5",
        }));
        var stable = Path.Combine(profile.Root, ".nexus", "nexus-windows", "NexusWindows.exe");
        List<ProcessInfo> Table(DateTime start, string launcher = "NexusWindows.exe") =>
        [
            new(800, 801, "NexusWindows.exe", $"\"{engine}\" --mcp --agent codex", engine, start),
            new(801, 802, launcher, "NexusWindows.exe --mcp --agent codex", launcher == "NexusWindows.exe" ? stable : @"C:\Windows\powershell.exe", start),
            new(802, 1, "codex.exe", "codex.exe app-server", @"C:\signed\codex.exe", start),
        ];
        var live = RuntimeObservation.Observe(Windows, profile.Folders, Table(birth));
        Assert.Equal(["3.9.5"], live.Versions);
        Assert.False(live.Unknown);
        Assert.Equal("codex", Assert.Single(live.Agents).Agent);
        live = RuntimeObservation.Observe(Windows, profile.Folders, Table(birth.AddSeconds(1)));
        Assert.Empty(live.Versions);
        Assert.True(live.Unknown);
        Assert.Null(Assert.Single(live.Agents).Version);  // an unattested connection stays in its AI's verdict
        Assert.Empty(RuntimeObservation.Observe(Windows, profile.Folders, Table(birth, "powershell.exe")).Versions);
        File.WriteAllText(assembly, "changed after receipt");
        live = RuntimeObservation.Observe(Windows, profile.Folders, Table(birth));
        Assert.Empty(live.Versions);
        Assert.True(live.Unknown);
    }

    [Fact]
    public void LoadedNativeAotRuntimeIsAttestedByItsExecutable()
    {
        // Native AOT: no NexusWindows.dll; the engine's receipt names the executable as the file holding its code.
        using var profile = new FakeProfile();
        var native = Path.Combine(profile.Root, ".nexus", "nexus-windows", "native", new string('a', 64));
        var engine = profile.Write(Path.Combine(native, "NexusWindows.exe"), "single-file engine");
        var digest = MaintenanceServices.Sha256(engine);
        var manifest = profile.Write(@".nexus\nexus-windows\installation.json", JsonSerializer.Serialize(new
        {
            NativeExecutable = engine, NativeArtifacts = new Dictionary<string, string> { ["NexusWindows.exe"] = digest },
        }));
        var birth = DateTime.UtcNow;
        profile.Write($@".nexus\status\runtime\windows-codex-800-{birth.Ticks}-session.json", JsonSerializer.Serialize(new
        {
            schema = 1, product = "windows", pid = 800, agent = "codex", state = "idle", processStartUtc = birth.ToString("O"),
            processStartTicks = birth.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), executable = engine, assembly = engine, assemblySha256 = digest, version = "3.17.0",
        }));
        var stable = Path.Combine(profile.Root, ".nexus", "nexus-windows", "NexusWindows.exe");
        List<ProcessInfo> table =
        [
            new(800, 801, "NexusWindows.exe", $"\"{engine}\" --mcp --agent codex", engine, birth),
            new(801, 802, "NexusWindows.exe", "NexusWindows.exe --mcp --agent codex", stable, birth),
            new(802, 1, "codex.exe", "codex.exe app-server", @"C:\signed\codex.exe", birth),
        ];
        var live = RuntimeObservation.Observe(Windows, profile.Folders, table);
        Assert.Equal(["3.17.0"], live.Versions);
        Assert.False(live.Unknown);
        // A partial framework-dependent record (an assembly without its host files) attests nothing.
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            NativeExecutable = engine, NativeArtifacts = new Dictionary<string, string> { ["NexusWindows.exe"] = digest, ["NexusWindows.dll"] = digest },
        }));
        live = RuntimeObservation.Observe(Windows, profile.Folders, table);
        Assert.Null(Assert.Single(live.Agents).Version);
        Assert.True(live.Unknown);
    }
    [Fact]
    public async Task UpdatePreservesRunningSessionsAndNeverReinstalls()
    {
        using var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true");
        var services = new FakeServices(profile.Folders);
        var maintainer = new Maintainer(services);
        Assert.Equal(2, await maintainer.RunAsync(current: true, force: false, TestContext.Current.CancellationToken));
        var row = Row(profile);
        Assert.Single(Status(profile).GetProperty("products").EnumerateArray());
        Assert.Equal(("3.9.5", "3.9.4"), (row.GetProperty("installedVersion").GetString(), row.GetProperty("loadedVersions")[0].GetString()));
        Assert.Equal((1, 1), (services.Installs, services.PluginRefresh));
        Assert.Equal("codex", Assert.Single(services.RunInstallerTargets));
        Assert.Equal(JsonValueKind.Null, row.GetProperty("log").ValueKind);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(MaintenanceUpdates.Root(profile.Folders), "logs")));
        Assert.DoesNotContain("NexusSystem", services.Downloads);
        Assert.Equal(2, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal(1, services.Installs);  // the retry reinstalls nothing

        services.Loaded = "3.9.5";
        Assert.Equal(0, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        var route = Row(profile).GetProperty("profiles")[0];
        Assert.Equal("runtime-verified", route.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, route.GetProperty("guideLoaded").ValueKind);

        // The hourly pause: no network, the receipt kept as observed.
        var saved = File.ReadAllText(MaintenanceUpdates.StatusPath(profile.Folders));
        var downloads = services.Downloads.Count;
        Assert.Equal(0, await maintainer.RunAsync(false, false, TestContext.Current.CancellationToken));
        Assert.Equal(downloads, services.Downloads.Count);
        Assert.Equal(saved, File.ReadAllText(MaintenanceUpdates.StatusPath(profile.Folders)));

        services.PluginVersion = "3.9.4";
        Assert.Equal(2, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Contains("distribution remains pending", Row(profile).GetProperty("profiles")[0].GetProperty("reason").GetString());
        services.PluginVersion = "3.9.5";
        profile.Write(@".nexus\nexussystem.json", """{"skip":["windows","claude-code"]}""");
        Assert.Equal(0, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Empty(Status(profile).GetProperty("products").EnumerateArray());
        File.Delete(LocalSettings.FilePath(profile.Folders));
    }

    [Fact]
    public async Task RepositoryAndUnknownEnginesAreObservedNeverReplaced()
    {
        using var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true");
        var services = new FakeServices(profile.Folders) { Loaded = "3.9.4" };
        services.Sources["windows"] = "repository";
        var maintainer = new Maintainer(services);
        Assert.Equal(0, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal((0, 1), (services.Installs, services.PluginRefresh));
        Assert.Empty(services.Downloads);
        var row = Row(profile);
        Assert.Equal(("kept", "3.9.5", "3.9.4", "3.9.4"), (row.GetProperty("state").GetString(), row.GetProperty("publishedVersion").GetString(),
            row.GetProperty("installedVersion").GetString(), row.GetProperty("loadedVersions")[0].GetString()));
        services.Sources["windows"] = "unknown";
        Assert.Equal(2, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal(0, services.Installs);
        Assert.StartsWith("Installation provenance is unknown", Row(profile).GetProperty("message").GetString());

        // A Browser repository build: its public catalogue plugin is refreshed, its engine never replaced or retried.
        File.WriteAllText(profile.Folders.CodexConfig, "[plugins.\"nexus-browser@nexus-plugins\"]\nenabled=true");
        services.Installed = new() { ["browser"] = "2.33.4" };
        services.Sources["browser"] = "repository";
        (services.Loaded, services.PluginVersion) = ("2.33.4", "2.33.5");
        Assert.Equal(0, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal("kept", Row(profile).GetProperty("state").GetString());
        services.Loaded = "2.33.3";
        Assert.Equal(2, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.False(Row(profile).GetProperty("retryable").GetBoolean());
        Assert.Equal(2, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal(0, services.Installs);
        Assert.Empty(services.Downloads);
    }

    [Fact]
    public async Task ExclusionNetworkAndInstallerFailuresStayExplicit()
    {
        using var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true");
        var services = new FakeServices(profile.Folders);
        var maintainer = new Maintainer(services);
        Directory.CreateDirectory(MaintenanceUpdates.Root(profile.Folders));
        using (new FileStream(Path.Combine(MaintenanceUpdates.Root(profile.Folders), "maintenance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(2, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        services.NetworkFailure = true;
        Assert.Equal(1, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal("failed", Status(profile).GetProperty("state").GetString());
        services.NetworkFailure = false;
        services.InstallerFailure = true;
        Assert.Equal(1, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        var failed = Row(profile);
        var log = failed.GetProperty("log").GetString()!;
        Assert.Equal("failed", failed.GetProperty("state").GetString());
        Assert.EndsWith("installer exit code 1). Installer output: " + log, failed.GetProperty("message").GetString());
        Assert.Contains("fixture cause", File.ReadAllText(log));
        Assert.StartsWith(Path.Combine(MaintenanceUpdates.Root(profile.Folders), "logs"), log, StringComparison.OrdinalIgnoreCase);
        (services.InstallerFailure, services.InstallerThrows) = (false, true);
        Assert.Equal(1, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        services.InstallerThrows = false;
        profile.Write(@".nexus\nexussystem.json", """{"autoUpdate":false}""");
        var downloads = services.Downloads.Count;
        Assert.Equal(0, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        Assert.Equal("disabled", Status(profile).GetProperty("state").GetString());
        Assert.Equal(downloads, services.Downloads.Count);
    }

    [Fact]
    public async Task TheEngineReplacesItselfOnlyWithANewerOrEqualPublishedEngine()
    {
        using var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true");
        var services = new FakeServices(profile.Folders) { Version = "1.6.40", EngineTag = "v1.6.36" };
        var maintainer = new Maintainer(services);
        Assert.Equal(2, await maintainer.RunAsync(false, true, TestContext.Current.CancellationToken));
        Assert.Contains("no downgrade", Status(profile).GetProperty("message").GetString());
        Assert.Null(services.InstalledEngine);

        (services.Version, services.EngineTag, services.EngineSha) = ("1.6.36", "v1.6.37", new string('c', 64));
        Assert.Equal(7, await maintainer.RunAsync(false, true, TestContext.Current.CancellationToken));  // the published engine's own verdict
        Assert.Equal((1, 0), (services.EngineRuns, services.Installs));
        Assert.Equal(MaintenanceUpdates.ExecutablePath(profile.Folders), services.InstalledEngine);
        Assert.Equal("1.6.37", Status(profile).GetProperty("latest").GetString());

        services.EngineSha = services.OwnSha;  // the engine already is the published one: it maintains the products
        Assert.Equal(2, await maintainer.RunAsync(false, true, TestContext.Current.CancellationToken));
        Assert.Equal((1, 1), (services.EngineRuns, services.Installs));
    }

    [Fact]
    public async Task ATestReleaseIsTheLocalCatalogueAndThisVerifiedEngine()
    {
        using var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true");
        var catalog = Path.Combine(profile.Root, "catalog");
        var engines = Path.Combine(profile.Root, "engines");
        profile.Write(@"catalog\.claude-plugin\marketplace.json", """{"metadata":{"version":"1.6.36"}}""");
        profile.Write(@"engines\SHA256SUMS.txt", $"{new string('a', 64)}  {MaintenanceServices.PublishedEngineName}\n");
        // The task's own command line (no --current, no --force): the published release is never consulted.
        var services = new FakeServices(profile.Folders) { TestRelease = new TestRelease(catalog, engines), EngineTag = "v1.6.35" };
        var maintainer = new Maintainer(services);
        Assert.Equal(2, await maintainer.RunAsync(false, false, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("NexusSystem", services.Downloads);
        Assert.Null(services.InstalledEngine);
        Assert.Equal(("1.6.36", "v1.6.36"), (Status(profile).GetProperty("latest").GetString(), Status(profile).GetProperty("catalogue").GetString()));
        Assert.StartsWith("Test release v1.6.36: catalogue " + catalog, Status(profile).GetProperty("message").GetString());
        Assert.Equal(1, services.Installs);

        // Verified by fingerprint: another engine, or a catalogue of another version, fails the cycle.
        File.Delete(MaintenanceUpdates.StatusPath(profile.Folders));
        services.OwnSha = new string('d', 64);
        Assert.Equal(1, await maintainer.RunAsync(false, false, TestContext.Current.CancellationToken));
        Assert.Contains("SHA-256 mismatch", Status(profile).GetProperty("message").GetString());
        (services.OwnSha, services.Version) = (new string('a', 64), "1.6.37");
        Assert.Equal(1, await maintainer.RunAsync(false, true, TestContext.Current.CancellationToken));
        Assert.Contains("not this engine's 1.6.37", Status(profile).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ACodexCatalogueFromAnotherSourceIsKeptAndReportedNotFailed()
    {
        using var profile = CodexProfile("[plugins.\"nexus-windows@nexus-plugins\"]\nenabled=true");
        var services = new FakeServices(profile.Folders) { Loaded = "3.9.5", KeptCatalogue = "Codex's catalogue comes from C:\\local: preserved." };
        var maintainer = new Maintainer(services);
        Assert.Equal(0, await maintainer.RunAsync(true, false, TestContext.Current.CancellationToken));
        var row = Row(profile);
        Assert.Equal("success", row.GetProperty("state").GetString());
        Assert.EndsWith("codex: Codex's catalogue comes from C:\\local: preserved.", row.GetProperty("message").GetString());
        var route = row.GetProperty("profiles")[0];
        Assert.Equal(("kept", false), (route.GetProperty("state").GetString(), route.GetProperty("activationPending").GetBoolean()));
    }

    [Fact]
    public void CodexCataloguesAreClassifiedBySource()
    {
        static (PluginMaintenance.CodexCatalogue, string?) Classify(string json, string? test = null) =>
            PluginMaintenance.ClassifyCodexCatalogue(JsonDocument.Parse(json).RootElement, test);
        Assert.Equal(PluginMaintenance.CodexCatalogue.Missing, Classify("""{"marketplaces":[{"name":"other"}]}""").Item1);
        Assert.Equal(PluginMaintenance.CodexCatalogue.Official,
            Classify("""{"marketplaces":[{"name":"nexus-plugins","marketplaceSource":{"sourceType":"git","source":"https://github.com/pironjulien/NexusSystem.git"}}]}""").Item1);
        const string local = """{"marketplaces":[{"name":"nexus-plugins","root":"D:\\a\\NexusSystem\\NexusSystem\\","marketplaceSource":{"sourceType":"local","source":"D:\\a\\NexusSystem\\NexusSystem"}}]}""";
        Assert.Equal((PluginMaintenance.CodexCatalogue.Other, @"D:\a\NexusSystem\NexusSystem"), Classify(local));
        Assert.Equal(PluginMaintenance.CodexCatalogue.TestCatalog, Classify(local, @"D:\a\NexusSystem\NexusSystem").Item1);
        Assert.Equal(PluginMaintenance.CodexCatalogue.Other, Classify(local, @"D:\elsewhere").Item1);
        Assert.Equal(PluginMaintenance.CodexCatalogue.Other,
            Classify("""{"marketplaces":[{"name":"nexus-plugins","marketplaceSource":{"sourceType":"git","source":"someone/fork"}}]}""", @"D:\a").Item1);
        Assert.Equal(PluginMaintenance.CodexCatalogue.Failed, Classify("""{"marketplaces":[{"name":"nexus-plugins"},{"name":"nexus-plugins"}]}""").Item1);
    }

    [Fact]
    public void TheCommandLineAcceptsTheTestReleaseOnlyWhole()
    {
        using var profile = new FakeProfile("catalog", "engines");
        var (catalog, engines) = (Path.Combine(profile.Root, "catalog"), Path.Combine(profile.Root, "engines"));
        Assert.True(MaintenanceOptions.TryParse([], out var plain));
        Assert.Equal(new MaintenanceOptions(false, false, null), plain);
        Assert.True(MaintenanceOptions.TryParse(["--current", "--force"], out var forced));
        Assert.Equal(new MaintenanceOptions(true, true, null), forced);
        Assert.True(MaintenanceOptions.TryParse(["--test-catalog", catalog + "\\", "--test-engines", engines], out var test));
        Assert.Equal(new TestRelease(catalog, engines), test.TestRelease);
        Assert.False(MaintenanceOptions.TryParse(["--test-catalog", catalog], out _));
        Assert.False(MaintenanceOptions.TryParse(["--test-catalog", catalog, "--test-engines", "relative"], out _));
        Assert.False(MaintenanceOptions.TryParse(["--test-catalog", catalog, "--test-engines", Path.Combine(profile.Root, "missing")], out _));
        Assert.False(MaintenanceOptions.TryParse(["--test-catalog", catalog, "--test-catalog", catalog, "--test-engines", engines], out _));
        Assert.False(MaintenanceOptions.TryParse(["--unknown"], out _));
    }

    [Fact]
    public async Task AFailingInstallerLeavesAReadableLog()
    {
        using var profile = new FakeProfile();
        var services = new MaintenanceServices(profile.Folders);
        var child = profile.Write("failing-installer.ps1",
            "param([switch]$UpdateOnly)\nWrite-Host ('host line '+$UpdateOnly)\nWrite-Output 'output line'\n[Console]::Error.WriteLine('stderr line')\nWrite-Error 'error line'\nexit 3\n");
        var log = services.InstallerLogPath("fixture");
        Assert.Equal(3, await services.RunInstallerAsync("Fixture maintenance", child, ["-UpdateOnly"], log, "codex", TestContext.Current.CancellationToken));
        var text = File.ReadAllText(log);
        foreach (var expected in new[] { "host line True", "output line", "stderr line", "error line", "Exit code: 3" }) Assert.Contains(expected, text);
        for (var i = 0; i < 25; i++) profile.Write(Path.Combine(Path.GetDirectoryName(log)!, $"old-{i}.log"), "old");
        services.InstallerLogPath("fixture");
        Assert.True(Directory.EnumerateFiles(Path.GetDirectoryName(log)!, "*.log").Count() <= 19);
    }

    [Fact]
    public async Task InstallerKeepsWindowsPowerShellModulesUnderAPowerShell7Parent()
    {
        // Run from pwsh (test.ps1, CI), the inherited module path names PowerShell 7 modules first.
        using var profile = new FakeProfile();
        var services = new MaintenanceServices(profile.Folders);
        var child = profile.Write("hash-installer.ps1", "if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) { exit 0 } else { exit 4 }\n");
        var log = services.InstallerLogPath("fixture");
        Assert.Equal(0, await services.RunInstallerAsync("Fixture maintenance", child, [], log, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReleaseRulesRefuseAmbiguityAndReadDeclaredParameters()
    {
        var hash = new string('A', 64);
        Assert.Equal(hash.ToLowerInvariant(), ReleaseRules.ExpectedSha256($"{hash}  get.ps1\n{new string('0', 64)} *other.exe\n", "get.ps1"));
        Assert.Throws<InvalidDataException>(() => ReleaseRules.ExpectedSha256("not a manifest", "get.ps1"));
        Assert.Throws<InvalidDataException>(() => ReleaseRules.ExpectedSha256($"{hash}  get.ps1\n{hash}  get.ps1", "get.ps1"));
        Assert.Throws<InvalidDataException>(() => ReleaseRules.ExpectedSha256($"{hash}  other.ps1", "get.ps1"));
        Assert.Equal("3.9.5", ReleaseRules.TagVersion("v3.9.5"));
        Assert.Throws<InvalidDataException>(() => ReleaseRules.TagVersion("v3.9.5-dev.1"));
        Assert.Equal(["Register"], ReleaseRules.ScriptParameters("param([switch]$Register)"));
        Assert.Equal(["UpdateOnly", "RefreshExistingProfiles", "Path"], ReleaseRules.ScriptParameters("""
            # Installer
            <# help #>
            [CmdletBinding()]
            param(
                [switch]$UpdateOnly, # comment with $Fake
                [Parameter(Mandatory = $false)][switch]$RefreshExistingProfiles,
                [string]$Path = "$env:USERPROFILE\x, $NotAParameter"
            )
            $Later = 1
            """));
        Assert.Empty(ReleaseRules.ScriptParameters("Write-Host 'no param block'\nparam([switch]$Late)"));
    }

    [Fact]
    public void JsonReplacementKeepsABackupAndTheRest()
    {
        using var profile = new FakeProfile();
        var settings = profile.Write(@".claude\settings.json", """{"extraKnownMarketplaces":{"nexus-plugins":{"source":{"source":"github"}}},"theme":"dark"}""");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings))!;
        node["extraKnownMarketplaces"]!["nexus-plugins"]!["autoUpdate"] = true;
        JsonWithBackup.Write(settings, node, profile.Folders);
        using var written = JsonDocument.Parse(File.ReadAllText(settings));
        Assert.True(written.RootElement.GetProperty("extraKnownMarketplaces").GetProperty("nexus-plugins").GetProperty("autoUpdate").GetBoolean());
        Assert.Equal("dark", written.RootElement.GetProperty("theme").GetString());
        var backup = Assert.Single(Directory.EnumerateFiles(Path.Combine(profile.Root, ".nexus", "pack", "backups")));
        Assert.DoesNotContain("autoUpdate", File.ReadAllText(backup));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(settings)!, "*.tmp"));
    }
}

public class CodexTomlTests
{
    [Fact]
    public void AFakeHeaderInsideAStringIsNeverATable()
    {
        var text = "model = \"x\"\nnotes = \"\"\"\n[mcp_servers.nexus-windows]\ncommand = \"evil\"\n\"\"\"\n[plugins.\"nexus-windows@nexus-plugins\"]\nenabled = true # on\n";
        Assert.Empty(CodexToml.ServerTables(text, "nexus-windows"));
        Assert.Equal("enabled", CodexToml.PluginState(text, "nexus-windows", "nexus-plugins"));
        Assert.Equal("missing", CodexToml.PluginState(text, "nexus-windows", "personal"));
        Assert.Equal(2, CodexToml.Tables(text).Count);
    }

    [Fact]
    public void StatesOfPluginsAndUnsupportedForms()
    {
        Assert.Equal("disabled", CodexToml.PluginState("[plugins.\"nexus-browser@nexus-plugins\"]\nenabled = false", "nexus-browser"));
        Assert.Equal("unknown", CodexToml.PluginState("plugins = { \"nexus-browser@nexus-plugins\" = { enabled = true } }", "nexus-browser"));
        Assert.Equal("unknown", CodexToml.PluginState("[plugins.\"nexus-browser@nexus-plugins\"]\nenabled = maybe", "nexus-browser"));
        Assert.Equal("enabled", CodexToml.PluginState("[plugins]\n\"nexus-browser@nexus-plugins\".enabled = true", "nexus-browser"));
        Assert.True(Assert.Single(CodexToml.ServerTables("mcp_servers.nexus-relay.url = \"http://x\"", "nexus-relay")).Unsupported);
        Assert.Throws<CodexTomlException>(() => CodexToml.Tables("[broken"));
        Assert.Throws<CodexTomlException>(() => CodexToml.Tables("key = \"unterminated"));
        Assert.Equal(["a b", "c.d", "\U0001F600"], CodexToml.KeyPath("\"a b\" . 'c.d' . \"\\U0001F600\""));
    }

    [Fact]
    public void OnlyTheInstallersOwnDeclarationsAreOwned()
    {
        using var profile = new FakeProfile();
        var engine = Path.Combine(profile.Root, ".nexus", "nexus-windows", "NexusWindows.exe");
        Assert.True(CodexToml.IsOwnedServer($"[mcp_servers.nexus-windows]\ncommand = '{engine}'\nargs = [\"--mcp\", \"--agent\", \"codex\"]\n", "nexus-windows", profile.Folders));
        Assert.False(CodexToml.IsOwnedServer($"[mcp_servers.nexus-windows]\ncommand = '{engine}'\nargs = [\"--mcp\", \"--agent\", \"claude\"]\n", "nexus-windows", profile.Folders));
        Assert.True(CodexToml.IsOwnedServer("[mcp_servers.nexus-relay]\nurl = \"http://127.0.0.1:16181/mcp?agent=codex\"\nbearer_token_env_var = \"NEXUS_MCP_TOKEN\"\n", "nexus-relay", profile.Folders));
        Assert.False(CodexToml.IsOwnedServer("[mcp_servers.nexus-relay]\nurl = \"http://127.0.0.1:16181/mcp?agent=codex\"\n", "nexus-relay", profile.Folders));
        Assert.True(CodexToml.IsOwnedServer("[mcp_servers.nexus-android]\ncommand = \"uvx\"\nargs = [\"nexus-android==1.2.3\"]\n", "nexus-android", profile.Folders));
        Assert.False(CodexToml.IsOwnedServer("[mcp_servers.nexus-android]\ncommand = \"uvx\"\nargs = [\"nexus-android\"]\n[mcp_servers.nexus-android.env]\nX = \"1\"\n", "nexus-android", profile.Folders));
        Assert.False(CodexToml.IsOwnedServer("[mcp_servers.nexus-android]\ncommand = \"docker\"\nargs = [\"run\"]\n", "nexus-android", profile.Folders));
    }
}

public class NativeTests
{
    [Fact]
    public void TheProcessListCarriesThisProcess()
    {
        var self = Assert.Single(NativeProcesses.Read(), p => p.ProcessId == Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, self.ExecutablePath, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileNameWithoutExtension(Environment.ProcessPath!), self.CommandLine, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(self.StartUtc!.Value, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow);
        Assert.True(self.ParentProcessId > 0);
    }

    [Fact]
    public void AuthenticodeNamesTheSigner()
    {
        var runtime = typeof(object).Assembly.Location;  // System.Private.CoreLib, signed by Microsoft
        Assert.True(Authenticode.IsSignedBy(runtime, "Microsoft Corporation"));
        Assert.False(Authenticode.IsSignedBy(runtime, "Anthropic, PBC"));
        using var profile = new FakeProfile();
        Assert.False(Authenticode.IsSignedBy(profile.Write("unsigned.exe", "not signed"), "Microsoft Corporation"));
        Assert.True(Authenticode.SubjectNames("CN=\"Anthropic, PBC\", O=\"Anthropic, PBC\", L=San Francisco", "Anthropic, PBC"));
        Assert.False(Authenticode.SubjectNames("CN=\"Not Anthropic, PBC\"", "Anthropic, PBC"));
    }
}
