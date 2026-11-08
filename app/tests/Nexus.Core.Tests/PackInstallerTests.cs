using System.Text;
using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

/// <summary>The pack's console, captured: text and colors are kept as one string (events included).</summary>
sealed class CapturedOutput : IPackOutput
{
    readonly StringBuilder text = new();

    public void Write(string value, PackColor color = PackColor.Gray)
    {
        lock (text) text.Append(value);
    }

    public override string ToString()
    {
        lock (text) return text.ToString();
    }
}

/// <summary>
/// The pack's real decisions on a throwaway profile: AI app detection without any PATH command, inert plugin routes and
/// product installers that record what they were asked (an installer that ignores the plugin writes its own direct Codex
/// declaration, as the native ones do). No network, installer, AI app or real configuration is touched.
/// </summary>
sealed class FakePack(UserFolders folders, CapturedOutput output) : PackServices(folders, output, json: true)
{
    public bool Git = true;
    public string? Codex = "signed-codex.exe";
    public string FailPlugin = "", OmitGuide = "", FailInstaller = "";
    public int MaintenanceInstalls;
    public List<string> Operations = [];

    public override IReadOnlyList<string> FindAiApps() => AiApps.FindKeys(Folders, _ => null);
    public override bool HasGit() => Git;
    public override string? CodexCli() => Codex;
    public override string? ClaudeCli() => null;
    public override int InstallGit(PackInstaller pack) { Operations.Add("git"); return 0; }
    public override void InstallMaintenance(PackOptions options) => MaintenanceInstalls++;
    public override void ShowBrowserGuide(string guide) => Operations.Add("guide");

    public override int InstallClaudePlugins(PackInstaller pack, IReadOnlyList<string> plugins, string catalog)
    {
        Operations.Add("claude:" + string.Join(',', plugins));
        return 0;
    }

    public override int InstallCodexMarketplace(PackInstaller pack, string cli, string catalog)
    {
        Operations.Add($"catalog:{cli}|{catalog}");
        return 0;
    }

    public override int InstallCodexPlugin(PackInstaller pack, string cli, string name)
    {
        Operations.Add("plugin:" + name);
        if (FailPlugin == name) return 1;
        AppendConfig($"[plugins.\"{name}@nexus-plugins\"]\nenabled = true");
        var root = Path.Combine(Path.GetDirectoryName(Folders.CodexConfig)!, "plugins", "cache", "nexus-plugins", name, "1.0.0");
        Directory.CreateDirectory(Path.Combine(root, ".codex-plugin"));
        Directory.CreateDirectory(Path.Combine(root, "skills", name));
        File.WriteAllText(Path.Combine(root, ".codex-plugin", "plugin.json"), $$"""{"name":"{{name}}","skills":"./skills/","mcpServers":"./.mcp.json"}""");
        File.WriteAllText(Path.Combine(root, ".mcp.json"), "{}");
        if (OmitGuide != name) File.WriteAllText(Path.Combine(root, "skills", name, "SKILL.md"), $"# {name} guide");
        return 0;
    }

    public override int RunProductInstaller(PackInstaller pack, PackProduct product, string work)
    {
        var targets = Environment.GetEnvironmentVariable("NEXUS_PACK_TARGETS") ?? "";
        Assert.Equal("1", Environment.GetEnvironmentVariable("NEXUS_PACK"));
        Operations.Add($"runtime:{product.Key}|{targets}");
        if (FailInstaller == product.Key) return 1;
        if (targets.Split(',').Contains("antigravity") && product.Antigravity.Count > 0)
        {
            var plugin = Path.Combine(Folders.AntigravityPlugins, product.Antigravity[0]);
            Directory.CreateDirectory(plugin);
            File.WriteAllText(Path.Combine(plugin, "plugin.json"), "{}");
        }
        if (!targets.Split(',').Contains("codex")) return 0;
        // An installer that does not detect the installed plugin writes its own direct declaration (native forms).
        var config = File.Exists(Folders.CodexConfig) ? File.ReadAllText(Folders.CodexConfig) : "";
        switch (product.Key)
        {
            case "windows":
                AppendConfig($"[mcp_servers.nexus-windows]\ncommand = '{Path.Combine(Folders.Home, ".nexus", "nexus-windows", "NexusWindows.exe")}'\nargs = [\"--mcp\", \"--agent\", \"codex\"]");
                break;
            case "android":
                AppendConfig("[mcp_servers.nexus-android]\nurl = \"http://127.0.0.1:16180/mcp\"\nbearer_token_env_var = \"NEXUS_MCP_TOKEN\"");
                break;
            case "browser":
                AppendConfig("[mcp_servers.nexus-browser]\nurl = \"http://127.0.0.1:16182/mcp?agent=codex\"\nbearer_token_env_var = \"NEXUS_MCP_TOKEN\"");
                break;
            case "relay" when CodexToml.PluginState(config, "nexus-relay") != "enabled":
                AppendConfig("[mcp_servers.nexus-relay]\nurl = \"http://127.0.0.1:16181/mcp?agent=codex\"\nbearer_token_env_var = \"NEXUS_MCP_TOKEN\"");
                break;
        }
        return 0;
    }

    void AppendConfig(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Folders.CodexConfig)!);
        File.AppendAllText(Folders.CodexConfig, $"\n{text}\n");
    }
}

public class PackInstallerTests
{
    static (int Code, string Output, FakePack Pack) Run(FakeProfile profile, string[]? only = null, string[]? skip = null, bool dryRun = true,
        Action<FakePack>? setup = null, string catalog = "test/local-catalog")
    {
        Assert.Null(Environment.GetEnvironmentVariable("CODEX_HOME")); // never the real Codex profile
        var output = new CapturedOutput();
        var pack = new FakePack(profile.Folders, output);
        setup?.Invoke(pack);
        var code = new PackInstaller(pack).Run(new PackOptions(only ?? [], skip ?? [], dryRun, NoGuide: true, catalog, Json: true));
        return (code, output.ToString(), pack);
    }

    static void Has(string output, params string[] texts)
    {
        foreach (var text in texts) Assert.True(output.Contains(text, StringComparison.Ordinal), $"missing: {text}\n--- output ---\n{output}");
    }

    static void HasNot(string output, params string[] texts)
    {
        foreach (var text in texts) Assert.False(output.Contains(text, StringComparison.Ordinal), $"unexpected: {text}\n--- output ---\n{output}");
    }

    static FakeProfile AllFour() => new(".claude", ".codex", @".gemini\antigravity", @"AppData\Roaming\Claude");

    [Fact]
    public void AllFourAiAppsEveryProduct()
    {
        using var profile = AllFour();
        var (code, output, _) = Run(profile);
        Assert.Equal(0, code);
        Has(output, "AI apps found: Claude Code, Claude Desktop, Codex, Antigravity",
            "NexusWindows  for Claude Code, Claude Desktop, Codex, Antigravity", "NexusAndroid  for Claude Code, Claude Desktop, Codex, Antigravity",
            "NexusBrowser  for Claude Code, Claude Desktop, Codex, Antigravity", "NexusRelay    for Claude Code, Codex, Antigravity",
            "NexusAGYmail  for Antigravity", "NexusKeypad   needs Logi Options+", "Prerequisites, once: none", "Dry run: nothing installed.");
        HasNot(output, "NexusFree", "Node.js", " uv");
    }

    [Fact]
    public void OnlyAProductAndAnAiApp()
    {
        using var profile = AllFour();
        var (code, output, _) = Run(profile, only: ["windows,codex"], setup: p => p.Git = false);
        Assert.Equal(0, code);
        Has(output, "NexusWindows  for Codex", "Prerequisites, once: Git (for Codex)");
        HasNot(output, "NexusAndroid  for", "NexusBrowser  for", "Git (for Claude Code)");
    }

    [Fact]
    public void GitOnlyWhenAPluginNeedsItAndItIsMissing()
    {
        using var profile = AllFour();
        var missing = Run(profile, only: ["claude-code"], setup: p => p.Git = false).Output;
        Has(missing, "Prerequisites, once: Git (for Claude Code)", "\"git\":true");
        var present = Run(profile, only: ["claude-code"]).Output;
        Has(present, "Prerequisites, once: none", "\"git\":false");
        HasNot(present, "Git (for Claude Code)");
    }

    [Fact]
    public void InstallersTakeTheSelection()
    {
        using var profile = AllFour();
        Has(Run(profile, only: ["browser", "codex"]).Output, "NexusBrowser  for Codex", "\"products\":[\"browser\"]");
        Has(Run(profile, only: ["android,codex"]).Output, "NexusAndroid  for Codex", "\"products\":[\"android\"]");
        // NexusRelay's Claude Code plugin comes from the catalog, but its installer is still told Claude Code: the host
        // stays Claude on a new PC.
        Has(Run(profile, only: ["relay"], skip: ["antigravity"]).Output, "NexusRelay    for Claude Code, Codex", "\"products\":[\"relay\"]",
            "\"relay\":[\"claude-code\",\"codex\"]");
        Has(Run(profile, only: ["windows,android,browser,agymail"]).Output,
            "\"windows\":[\"claude-code\",\"claude-desktop\",\"codex\",\"antigravity\"]", "\"android\":[\"claude-code\",\"claude-desktop\",\"codex\",\"antigravity\"]",
            "\"browser\":[\"claude-code\",\"claude-desktop\",\"codex\",\"antigravity\"]", "\"agymail\":[\"antigravity\"]");
    }

    [Fact]
    public void NexusFreeOnlyWhenNamed()
    {
        using var profile = AllFour();
        Has(Run(profile, only: ["free"]).Output, "NexusFree     for Claude Code, Codex, Antigravity", "\"products\":[\"free\"]");
    }

    [Fact]
    public void ThisPcsChoicesAddToSkip()
    {
        using var profile = new FakeProfile(".claude", ".codex", @".gemini\antigravity", @"AppData\Roaming\Claude", ".nexus");
        profile.Write(@".nexus\nexussystem.json", """{"skip": ["claude-desktop", "nonsense"]}""");
        var output = Run(profile, only: ["windows"]).Output;
        Has(output, "NexusWindows  for Claude Code, Codex, Antigravity", "unknown name 'nonsense' ignored");
        HasNot(output, "NexusWindows  for Claude Code, Claude Desktop");
        profile.Write(@".nexus\nexussystem.json", "{ not json");
        Has(Run(profile, only: ["windows"]).Output, "is not readable JSON: ignored", "NexusWindows  for Claude Code, Claude Desktop, Codex, Antigravity");
    }

    [Fact]
    public void TurnedOffByTheUserIsKept()
    {
        using var profile = new FakeProfile(".claude", ".codex");
        profile.Write(@".codex\config.toml", "[mcp_servers.nexus-windows]\ncommand = \"npx\"\nenabled = false\n\n[mcp_servers.nexus-browser]\ncommand = \"nexus-browser.exe\"\nenabled = false\n");
        var (code, output, _) = Run(profile, only: ["windows,browser"]);
        Assert.Equal(0, code);
        Has(output, "NexusWindows  Codex: turned off by you, left as it is", "NexusBrowser  Codex: turned off by you, left as it is",
            "\"products\":[\"windows\",\"browser\"]", "\"windows\":[\"claude-code\"]");
    }

    [Fact]
    public void SkipAsOneCommaSeparatedString()
    {
        using var profile = AllFour();
        var output = Run(profile, skip: ["codex, browser"]).Output;
        Has(output, "NexusWindows  for Claude Code, Claude Desktop, Antigravity");
        HasNot(output, "NexusBrowser  for");
    }

    [Fact]
    public void KeypadNeedsLogiOptions()
    {
        using var all = AllFour();
        Has(Run(all, only: ["keypad"]).Output, "NexusKeypad   needs Logi Options+", "Nothing to install with these options.");
        using var logi = new FakeProfile(".claude", @"AppData\Local\Logi\LogiPluginService");
        Has(Run(logi, only: ["keypad"]).Output, "NexusKeypad   for your Logi Options+");
    }

    [Fact]
    public void RelayNeedsTwoAiAppsAgymailAntigravity()
    {
        using var codex = new FakeProfile(".codex");
        Has(Run(codex).Output, "NexusWindows  for Codex", "NexusRelay    needs two of Claude Code, Codex, Antigravity", "NexusAGYmail  for Antigravity only");
        using var codexAgy = new FakeProfile(".codex", @".gemini\antigravity");
        Has(Run(codexAgy, only: ["relay"]).Output, "NexusRelay    for Codex, Antigravity");
    }

    [Fact]
    public void UnknownNameRefused()
    {
        using var profile = AllFour();
        var (code, output, _) = Run(profile, only: ["nexuswindows"]);
        Assert.Equal(2, code);
        Has(output, "Unknown name 'nexuswindows'");
    }

    [Fact]
    public void ClaudeDesktopFromEveryLayoutAndNotAFolderItLeft()
    {
        using var desktop = new FakeProfile(@"AppData\Roaming\Claude");
        var output = Run(desktop).Output;
        Has(output, "AI apps found: Claude Code, Claude Desktop", "NexusWindows  for Claude Code, Claude Desktop", "NexusRelay    needs two of");
        HasNot(output, "AI apps found: Claude Code, Claude Desktop, Codex");
        using var store = new FakeProfile(@"AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude");
        Has(Run(store).Output, "AI apps found: Claude Code, Claude Desktop");
        using var left = new FakeProfile(@"AppData\Roaming\Claude");
        left.Write(@"AppData\Roaming\Claude\.user-data-left.anthropicpbc.claude_fnn82j28hfe8t", "");
        var (code, none, _) = Run(left);
        Assert.Equal(1, code);
        Has(none, "No AI app found");
        HasNot(none, "AI apps found");
    }

    [Fact]
    public void AntigravityInstalledNeverOpened()
    {
        using var profile = new FakeProfile(@"AppData\Local\Programs\Antigravity");
        var output = Run(profile).Output;
        Has(output, "AI apps found: Antigravity", "Prerequisites, once: none");
        HasNot(output, "Git (");
    }

    [Fact]
    public void JsonEvents()
    {
        using var profile = AllFour();
        var output = Run(profile, only: ["windows,codex"]).Output;
        Has(output, "\"type\":\"found\"", "\"ais\":[\"claude-code\",\"claude-desktop\",\"codex\",\"antigravity\"]", "\"type\":\"plan\"",
            "\"products\":[\"windows\"]", "\"ais\":[\"codex\"]");
    }

    // Codex: the catalog plugins first, then the native installers; their duplicates are removed only when exact.

    static FakeProfile CodexProfile(string config = "")
    {
        var profile = new FakeProfile(".codex");
        profile.Write(@".codex\config.toml", config);
        return profile;
    }

    static string Config(FakeProfile profile) => File.ReadAllText(profile.Folders.CodexConfig);

    [Fact]
    public void CodexFreshInstallationPluginsFirstDuplicatesRemoved()
    {
        // Codex and Antigravity: NexusRelay needs two AI apps.
        using var profile = CodexProfile();
        Directory.CreateDirectory(Path.Combine(profile.Root, @".gemini\antigravity"));
        var (code, output, pack) = Run(profile, only: ["windows,android,browser,relay"], dryRun: false);
        Assert.True(code == 0, output);
        foreach (var name in new[] { "nexus-windows", "nexus-android", "nexus-browser", "nexus-relay" })
        {
            Assert.True(PluginMaintenance.CodexPluginInstalled(name, profile.Folders), $"{name} guide not installed");
            Assert.Empty(CodexToml.ServerTables(Config(profile), name));
        }
        Assert.Equal(["catalog:signed-codex.exe|test/local-catalog", "plugin:nexus-windows", "plugin:nexus-android", "plugin:nexus-browser", "plugin:nexus-relay",
            "runtime:windows|codex,antigravity", "runtime:android|codex,antigravity", "runtime:browser|codex,antigravity", "runtime:relay|codex,antigravity"], pack.Operations);
        Assert.Equal(1, pack.MaintenanceInstalls);
        Has(output, "Summary", "\u2713 ok", "Done. Ask your AI, for example:");
    }

    [Fact]
    public void CodexPluginFailureIsVisibleAndTheDirectDeclarationKept()
    {
        using var profile = CodexProfile();
        var (code, output, _) = Run(profile, only: ["android"], dryRun: false, setup: p => p.FailPlugin = "nexus-android");
        Assert.Equal(1, code);
        Assert.Single(CodexToml.ServerTables(Config(profile), "nexus-android"));
        Has(output, "1 item(s) failed");
    }

    [Fact]
    public void CodexMissingGuideIsNeverComplete()
    {
        using var profile = CodexProfile();
        var (code, _, _) = Run(profile, only: ["browser"], dryRun: false, setup: p => p.OmitGuide = "nexus-browser");
        Assert.Equal(1, code);
        Assert.Single(CodexToml.ServerTables(Config(profile), "nexus-browser"));
    }

    [Fact]
    public void FailedInstallerFailsEveryCellItServesAndNoMaintenanceWithoutSuccess()
    {
        using var profile = CodexProfile();
        var (code, output, pack) = Run(profile, only: ["android"], dryRun: false, setup: p => { p.FailInstaller = "android"; p.Codex = null; });
        Assert.Equal(1, code);
        Has(output, "The installed Codex engine with a valid OpenAI signature was not found.", "\"state\":\"missing\"");
        Assert.Equal(0, pack.MaintenanceInstalls);
    }

    [Fact]
    public void CodexDisabledPluginStaysByteIdentical()
    {
        const string disabled = "[plugins.\"nexus-android@nexus-plugins\"]\nenabled = false\n[mcp_servers.nexus-android]\ncommand = \"uvx\"\nargs = [\"nexus-android\"]";
        using var profile = CodexProfile(disabled);
        var (code, _, pack) = Run(profile, only: ["android"], dryRun: false);
        Assert.Equal(0, code);
        Assert.Equal(disabled, Config(profile));
        Assert.DoesNotContain(pack.Operations, o => o.StartsWith("plugin:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[ plugins . 'nexus-android@nexus-plugins' ] # kept\n'enabled' = false\n", "android")]
    [InlineData("[plugins.\"nexus\\u002dandroid@nexus-plugins\"]\nenabled = false\n", "android")]
    [InlineData("[plugins.\"nexus\\U0000002Dandroid@nexus-plugins\"]\nenabled = false\n", "android")]
    [InlineData("[ mcp_servers . 'nexus-android' ]\n'enabled' = false\ncommand = 'uvx'\nargs = ['nexus-android']\n", "android")]
    [InlineData("[ mcp_servers . 'nexus-browser' ]\ncommand = 'docker'\nargs = ['run','custom-browser']\n", "browser")]
    [InlineData("[mcp_servers]\n'nexus-browser' = { command = 'docker', args = ['run','custom-browser'] }\n", "browser")]
    [InlineData("mcp_servers . 'nexus-browser' . command = 'docker'\nmcp_servers . 'nexus-browser' . args = ['run','custom-browser']\n", "browser")]
    [InlineData("[plugins]\n'nexus-android@nexus-plugins' = { enabled = false }\n", "android")]
    [InlineData("plugins = { 'nexus-android@nexus-plugins' = { enabled = false } }\n", "android")]
    [InlineData("plugins . 'nexus-android@nexus-plugins' . enabled = false\n", "android")]
    [InlineData("[ plugins . 'nexus-windows@personal' ]\nenabled = true\n", "windows")]
    [InlineData("[mcp_servers.nexus-browser]\ncommand = \"docker\"\nargs = [\"run\", \"my-browser\"]\n[other]\nvalue = \"kept\"", "browser")]
    public void CodexUserChoicesStayByteIdentical(string config, string product)
    {
        using var profile = CodexProfile(config);
        var (code, output, pack) = Run(profile, only: [product], dryRun: false);
        Assert.True(code == 0, output);
        Assert.Equal(config, Config(profile));
        Assert.DoesNotContain(pack.Operations, o => o.StartsWith("plugin:", StringComparison.Ordinal) || o.StartsWith("catalog:", StringComparison.Ordinal) ||
            o.StartsWith($"runtime:{product}|codex", StringComparison.Ordinal));
    }

    [Fact]
    public void CodexOnlyTheOwnedTableIsRemovedNeighboursKept()
    {
        const string prefix = "instructions = \"\"\"\n[plugins.\"nexus-android@nexus-plugins\"]\nenabled = false\n[mcp_servers.nexus-browser]\ncommand = \"docker\"\n\"\"\"\n\n";
        const string owned = "[ mcp_servers . 'nexus-android' ] # original\ncommand = 'uvx'\nargs = ['--python','3.14','nexus-android']\n[other]\ntext = 'kept'\n[ plugins . 'nexus-android@nexus-plugins' ]\nenabled = true\n";
        using var profile = CodexProfile(prefix + owned);
        Assert.True(CodexToml.IsOwnedServer(Config(profile), "nexus-android", profile.Folders));
        Assert.True(CodexToml.RemoveOwnedServer(profile.Folders, "nexus-android"));
        Assert.Equal(prefix + "[other]\ntext = 'kept'\n[ plugins . 'nexus-android@nexus-plugins' ]\nenabled = true\n", Config(profile));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(profile.Root, @".nexus\pack\backups"), "codex-config-*.toml"));
        Assert.False(CodexToml.RemoveOwnedServer(profile.Folders, "nexus-android"));
    }

    [Fact]
    public void CodexUserDeclarationIsNeverRemoved()
    {
        const string custom = "[mcp_servers.nexus-android]\ncommand = \"docker\"\nargs = [\"run\"]\n[plugins.\"nexus-android@nexus-plugins\"]\nenabled = true\n";
        using var profile = CodexProfile(custom);
        Assert.Throws<InvalidOperationException>(() => CodexToml.RemoveOwnedServer(profile.Folders, "nexus-android"));
        Assert.Equal(custom, Config(profile));
    }

    /// <summary>A Codex command line that answers like the signed engine, recording the commands that change something.</summary>
    sealed class CatalogCli(UserFolders folders, CapturedOutput output) : PackServices(folders, output)
    {
        public string Listed = """{"marketplaces":[]}""";
        public bool ListFailure;
        public List<string> Changes = [];

        public override (int Code, string Output, string Error) RunCaptured(string file, IEnumerable<string> arguments) =>
            ListFailure ? (1, "", "failed") : (0, Listed, "");

        public override int RunVisible(string file, IEnumerable<string> arguments, bool cleanModulePath = false)
        {
            Changes.Add(string.Join('|', arguments.Skip(2)));
            return 0;
        }

        protected override void ApproveHooks(PackInstaller pack, string cli, string? pluginId, string who, string which) => Changes.Add("hooks");
    }

    static string Marketplace(string root, string type, string source) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["marketplaces"] = new[] { new Dictionary<string, object> { ["name"] = "nexus-plugins", ["root"] = root, ["marketplaceSource"] = new Dictionary<string, string> { ["sourceType"] = type, ["source"] = source } } },
        });

    [Theory]
    [InlineData("https://github.com/pironjulien/NexusSystem.git", "pironjulien/NexusSystem")]
    [InlineData("pironjulien/NexusSystem", "https://github.com/pironjulien/NexusSystem.git")]
    [InlineData("https://github.com/pironjulien/NexusSystem", "pironjulien/NexusSystem")]
    [InlineData("git@github.com:pironjulien/NexusSystem.git", "pironjulien/NexusSystem")]
    [InlineData("ssh://git@github.com/pironjulien/NexusSystem.git", "pironjulien/NexusSystem")]
    public void CodexEquivalentPublicCatalogIsOnlyRefreshed(string existing, string requested)
    {
        using var profile = new FakeProfile();
        var cli = new CatalogCli(profile.Folders, new CapturedOutput()) { Listed = Marketplace(@"C:\cache\nexus-plugins", "git", existing) };
        Assert.Equal(0, cli.InstallCodexMarketplace(new PackInstaller(cli), "codex.exe", requested));
        Assert.Equal(["upgrade|nexus-plugins|--json", "hooks"], cli.Changes);
    }

    [Fact]
    public void CodexOtherCatalogSourcesAndFailuresArePreserved()
    {
        using var profile = new FakeProfile("catalogue");
        var local = Path.Combine(profile.Root, "catalogue");
        var custom = new CatalogCli(profile.Folders, new CapturedOutput()) { Listed = Marketplace(@"C:\cache\custom", "git", "https://github.com/team/custom-catalogue.git") };
        Assert.NotEqual(0, custom.InstallCodexMarketplace(new PackInstaller(custom), "codex.exe", "pironjulien/NexusSystem"));
        Assert.Empty(custom.Changes);

        var same = new CatalogCli(profile.Folders, new CapturedOutput()) { Listed = Marketplace(local, "local", local) };
        Assert.Equal(0, same.InstallCodexMarketplace(new PackInstaller(same), "codex.exe", local));
        Assert.NotEqual(0, same.InstallCodexMarketplace(new PackInstaller(same), "codex.exe", "pironjulien/NexusSystem"));
        Assert.Empty(same.Changes);

        var missing = new CatalogCli(profile.Folders, new CapturedOutput());
        Assert.Equal(0, missing.InstallCodexMarketplace(new PackInstaller(missing), "codex.exe", "pironjulien/NexusSystem"));
        Assert.Equal(["add|pironjulien/NexusSystem|--json", "upgrade|nexus-plugins|--json", "hooks"], missing.Changes);

        var failed = new CatalogCli(profile.Folders, new CapturedOutput()) { ListFailure = true };
        Assert.NotEqual(0, failed.InstallCodexMarketplace(new PackInstaller(failed), "codex.exe", "pironjulien/NexusSystem"));
        Assert.Empty(failed.Changes);
    }

    [Fact]
    public void ClaudeWithoutItsCommandGetsTheDocumentedSettings()
    {
        using var profile = new FakeProfile(".claude", "catalogue");
        profile.Write(@".claude\settings.json", """{"enabledPlugins": {"other@elsewhere": true}, "theme": "dark"}""");
        var output = new CapturedOutput();
        var services = new ClaudeSettingsOnly(profile.Folders, output);
        var pack = new PackInstaller(services);
        Assert.Equal(0, services.InstallClaudePlugins(pack, ["nexus-windows", "nexus-relay"], Path.Combine(profile.Root, "catalogue")));
        var settings = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(profile.Folders.ClaudeSettings))!;
        Assert.Equal("directory", settings["extraKnownMarketplaces"]!["nexus-plugins"]!["source"]!["source"]!.GetValue<string>());
        Assert.True(settings["enabledPlugins"]!["nexus-relay@nexus-plugins"]!.GetValue<bool>());
        Assert.True(settings["enabledPlugins"]!["other@elsewhere"]!.GetValue<bool>());
        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(profile.Root, @".nexus\pack\backups")));
        Assert.Equal(0, services.InstallClaudePlugins(pack, ["nexus-windows"], "pironjulien/NexusSystem"));
        Has(output.ToString(), "Declared in", "Already declared in");
    }

    sealed class ClaudeSettingsOnly(UserFolders folders, CapturedOutput output) : PackServices(folders, output)
    {
        public override string? ClaudeCli() => null;
    }

    [Fact]
    public void EventLinesAreCompactJson()
    {
        var line = PackInstaller.EventLine(new Dictionary<string, object?> { ["type"] = "cell", ["ok"] = true, ["code"] = 2, ["ais"] = new List<string> { "codex" }, ["none"] = null });
        Assert.Equal("NEXUS_PACK_EVENT {\"type\":\"cell\",\"ok\":true,\"code\":2,\"ais\":[\"codex\"],\"none\":null}", line);
    }
}

public class EngineCommandTests
{
    [Fact]
    public void HourlyOptionsStayAsTheyWere()
    {
        Assert.Equal(EngineMode.Hourly, EngineCommand.Parse([])!.Mode);
        var hourly = EngineCommand.Parse(["--current", "--force"])!;
        Assert.True(hourly.Hourly!.Current && hourly.Hourly.Force);
        Assert.Null(EngineCommand.Parse(["--unknown"]));
    }

    [Fact]
    public void InstallTakesThePackOptions()
    {
        var command = EngineCommand.Parse(["--install", "--only", "windows,browser", "--skip", "codex", "--dry-run", "--no-guide", "--json", "--catalog", "team/catalog"])!;
        Assert.Equal(EngineMode.Install, command.Mode);
        var pack = command.Pack!;
        Assert.Equal(["windows,browser"], pack.Only);
        Assert.Equal(["codex"], pack.Skip);
        Assert.True(pack.DryRun && pack.NoGuide && pack.Json);
        Assert.Equal(("team/catalog", (string?)null), (pack.Catalog, pack.MaintenanceFrom));
        Assert.Equal(PackOptions.OfficialCatalog, EngineCommand.Parse(["--install"])!.Pack!.Catalog);
        Assert.Null(EngineCommand.Parse(["--install", "--only"]));
        Assert.Null(EngineCommand.Parse(["--install", "--dry-run", "--dry-run"]));
        // --maintenance-from is a test option: a local catalogue and a folder holding SHA256SUMS.txt.
        Assert.Null(EngineCommand.Parse(["--install", "--maintenance-from", @"C:\nowhere"]));
    }

    [Fact]
    public void MaintenanceModes()
    {
        Assert.True(EngineCommand.Parse(["--install-maintenance", "--start"])!.Start);
        Assert.False(EngineCommand.Parse(["--install-maintenance"])!.Start);
        Assert.True(EngineCommand.Parse(["--maintain", "--force"])!.Force);
        Assert.Equal(EngineMode.Status, EngineCommand.Parse(["--status"])!.Mode);
        Assert.Null(EngineCommand.Parse(["--status", "--force"]));
        Assert.Null(EngineCommand.Parse(["--maintain", "--start"]));
    }
}
