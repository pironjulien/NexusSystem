using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>
/// The two catalogs (Claude Code: .claude-plugin + plugins/, Codex: .agents/plugins + codex/). A product moves to its
/// native form only once its native release and get.ps1 are published (what is installed is always published before
/// the installer that depends on it). Today: NexusWindows and NexusBrowser still start their pinned PyPI server with
/// uvx (pyproject.toml and uv.lock beside it); NexusAndroid (native 0.22.0) and NexusRelay (native 1.10.0) are their local daemon;
/// NexusKeypad calls its native helper; NexusAGYmail is installed by its native get.ps1 (Antigravity only).
/// </summary>
public class CatalogTests
{
    const string Marketplace = "nexus-plugins";
    static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$");
    static readonly string[] Plugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay", "nexus-keypad"];
    static readonly string[] CodexPlugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay"];

    /// <summary>Plugins still served from PyPI, and their package (NexusWindows for Claude: nexus-windows-claude).</summary>
    static readonly Dictionary<string, string> PyPI = new()
    {
        ["nexus-windows"] = "nexus-windows-claude", ["nexus-browser"] = "nexus-browser",
    };

    /// <summary>Codex plugins served from another repository (its Codex edition).</summary>
    static readonly Dictionary<string, string> CodexUrls = new() { ["nexus-windows"] = "https://github.com/pironjulien/NexusWindows-Codex.git" };

    /// <summary>Plugins served by their product's local daemon: exactly this declaration, the token read from the user variable.</summary>
    static readonly Dictionary<string, (string Claude, string Codex)> Daemons = new()
    {
        ["nexus-relay"] = (RelayClaude, RelayCodex),
        ["nexus-android"] = ("""{"nexus-android":{"type":"http","url":"http://127.0.0.1:16180/mcp","headers":{"Authorization":"Bearer ${NEXUS_MCP_TOKEN}"}}}""",
            """{"nexus-android":{"url":"http://127.0.0.1:16180/mcp","bearer_token_env_var":"NEXUS_MCP_TOKEN","startup_timeout_sec":60}}"""),
    };

    const string RelayClaude = """{"nexus-relay":{"type":"http","url":"http://127.0.0.1:16181/mcp?agent=claude","headers":{"Authorization":"Bearer ${NEXUS_MCP_TOKEN}"}}}""";
    const string RelayCodex = """{"nexus-relay":{"url":"http://127.0.0.1:16181/mcp?agent=codex","bearer_token_env_var":"NEXUS_MCP_TOKEN","startup_timeout_sec":60}}""";

    static JsonArray Entries(string marketplace) => Repository.Json(marketplace)["plugins"]!.AsArray();

    static string Version(string name) => (string)Repository.Json($"plugins/{name}/.claude-plugin/plugin.json")["version"]!;

    public static TheoryData<string> PluginNames => new(Plugins);

    public static TheoryData<string> CodexNames => new(CodexPlugins);

    public static TheoryData<string> PyPINames => new(PyPI.Keys);

    [Fact]
    public void ClaudeMarketplaceListsEveryPluginFolder()
    {
        var market = Repository.Json(".claude-plugin/marketplace.json");
        Assert.Equal(Marketplace, (string?)market["name"]);
        Assert.False(string.IsNullOrEmpty((string?)market["owner"]?["name"]));
        Assert.Matches(SemVer, (string?)market["metadata"]?["version"] ?? "");
        Assert.Equal(Plugins, Entries(".claude-plugin/marketplace.json").Select(e => (string?)e!["name"]));
        var folders = Directory.GetDirectories(Repository.PathOf("plugins")).Select(Path.GetFileName).Order();
        Assert.Equal(Plugins.Order(), folders);
        Assert.DoesNotContain("nexus-indesign", folders);  // a personal plugin, never public
    }

    [Theory]
    [MemberData(nameof(PluginNames))]
    public void ClaudePluginManifestMatchesItsEntry(string name)
    {
        var entry = Entries(".claude-plugin/marketplace.json").Single(e => (string?)e!["name"] == name)!;
        var manifest = Repository.Json($"plugins/{name}/.claude-plugin/plugin.json");
        Assert.Equal($"./plugins/{name}", (string?)entry["source"]);
        Assert.Equal(name, (string?)manifest["name"]);
        Assert.Matches(SemVer, (string?)manifest["version"] ?? "");
        Assert.Equal((string?)manifest["version"], (string?)entry["version"]);
        foreach (var field in new[] { "description", "license", "repository", "homepage" })
        {
            Assert.False(string.IsNullOrEmpty((string?)manifest[field]), $"{name}: plugin.json {field} missing");
            Assert.False(string.IsNullOrEmpty((string?)entry[field]), $"{name}: marketplace {field} missing");
        }
        if (name == "nexus-relay") Assert.Contains("daemon", (string?)entry["description"], StringComparison.OrdinalIgnoreCase);
        if (name == "nexus-browser") Assert.Contains("extension", (string?)entry["description"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("./.claude-plugin/icon.png", (string?)manifest["icon"]);
        Assert.True(Repository.PngSize($"plugins/{name}/.claude-plugin/icon.png") is var (w, h) && w == h && w is >= 512 and <= 2048,
            $"{name}: icon must be a square PNG of 512 to 2048 px");
        Assert.True(File.Exists(Repository.PathOf($"plugins/{name}/LICENSE")), $"{name}: LICENSE missing");
    }

    [Theory]
    [MemberData(nameof(PyPINames))]
    public void PyPIPluginStartsItsPinnedAndLockedServer(string name)
    {
        var package = PyPI[name];
        var version = Version(name);
        var servers = Repository.Json($"plugins/{name}/.mcp.json")["mcpServers"]!.AsObject();
        var server = Assert.Single(servers);
        Assert.Equal(name, server.Key);
        // The directory's validator reads exactly "uvx <package>==<version>", with uv.lock next to the manifest.
        Assert.Equal("uvx", (string?)server.Value!["command"]);
        Assert.Equal(["--python", "3.14", $"{package}=={version}"], server.Value["args"]!.AsArray().Select(a => (string?)a));
        Assert.DoesNotContain(server.Value["env"]?.AsObject().Select(e => e.Key) ?? [], key => key.StartsWith("UV_", StringComparison.Ordinal));
        var project = Repository.Text($"plugins/{name}/pyproject.toml");
        Assert.Contains($"dependencies = [\"{package}=={version}\"]", project);
        Assert.Matches($"(?m)^version = \"{Regex.Escape(version)}\"", project);
        var lockFile = Repository.Text($"plugins/{name}/uv.lock").ReplaceLineEndings("\n");
        Assert.Contains($"name = \"{package}\"\nversion = \"{version}\"", lockFile);
        Assert.Contains("sha256:", lockFile);
    }

    [Fact]
    public void ClaudeRelayHooksUseTheStableEngine()
    {
        // Claude Code runs hooks with bash: the engine NexusRelay's installer keeps at ~/.nexus/nexus-relay/bin. The
        // edit guard on file edits, the release after the tool and at the end, the notice of unread messages.
        var events = Repository.Json("plugins/nexus-relay/hooks/hooks.json")["hooks"]!.AsObject();
        var expected = new Dictionary<string, (string? Matcher, string Arguments)>
        {
            ["PreToolUse"] = ("Edit|Write|MultiEdit|NotebookEdit", "--edit-guard claude"),
            ["PostToolUse"] = ("Edit|Write|MultiEdit|NotebookEdit", "--edit-guard claude --finish-tool"),
            ["PostToolUseFailure"] = ("Edit|Write|MultiEdit|NotebookEdit", "--edit-guard claude --finish-tool"),
            ["Stop"] = (null, "--edit-guard claude --finish-session"),
            ["SessionEnd"] = (null, "--edit-guard claude --finish-session"),
            ["SessionStart"] = (null, "--notice"),
            ["UserPromptSubmit"] = (null, "--notice"),
        };
        Assert.Equal(expected.Keys.Order(), events.Select(e => e.Key).Order());
        foreach (var (name, (matcher, arguments)) in expected)
        {
            var group = Assert.Single(events[name]!.AsArray())!;
            Assert.Equal(matcher, (string?)group["matcher"]);
            var hook = Assert.Single(group["hooks"]!.AsArray())!;
            Assert.Equal($"\"$HOME/.nexus/nexus-relay/bin/NexusRelay.exe\" {arguments} || true", (string?)hook["command"]);
        }
    }

    public static TheoryData<string> DaemonNames => new(Daemons.Keys);

    [Theory]
    [MemberData(nameof(DaemonNames))]
    public void DaemonPluginIsItsLocalDaemon(string name)
    {
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Daemons[name].Claude), Repository.Json($"plugins/{name}/.mcp.json")["mcpServers"]), $"{name}: Claude declaration");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Daemons[name].Codex), Repository.Json($"codex/{name}/.mcp.json")["mcpServers"]), $"{name}: Codex declaration");
        Assert.False(File.Exists(Repository.PathOf($"plugins/{name}/pyproject.toml")), "a daemon plugin starts nothing");
        Assert.False(File.Exists(Repository.PathOf($"plugins/{name}/uv.lock")), "a daemon plugin starts nothing");
    }

    [Fact]
    public void KeypadPluginCallsOnlyItsNativeHelper()
    {
        Assert.False(File.Exists(Repository.PathOf("plugins/nexus-keypad/.mcp.json")), "no MCP server");
        var hooks = Repository.Json("plugins/nexus-keypad/hooks/hooks.json");
        Assert.Equal(["./register.tsx"], hooks["modules"]!.AsArray().Select(m => (string?)m));
        foreach (var module in new[] { "register.tsx", "quota.ts" })
            Assert.True(File.Exists(Repository.PathOf($"plugins/nexus-keypad/hooks/{module}")), $"keypad module {module} missing");
        var events = hooks["hooks"]!.AsObject();
        Assert.Contains("Stop", events.Select(e => e.Key));
        foreach (var (eventName, groups) in events)
        {
            var hook = Assert.Single(Assert.Single(groups!.AsArray())!["hooks"]!.AsArray())!;
            Assert.Equal("powershell", (string?)hook["shell"]);
            Assert.True((bool?)hook["async"], $"{eventName}: the pad never slows Claude down");
            Assert.Equal($"try {{ & (Join-Path $env:LOCALAPPDATA 'NexusKeypad\\hook\\nexuskeypad-hook.exe') claude {eventName} }} catch {{}}",
                (string?)hook["command"]);
        }
    }

    [Theory]
    [MemberData(nameof(PluginNames))]
    public void ClaudePluginHasOneRealSkill(string name)
    {
        var skills = Directory.GetDirectories(Repository.PathOf($"plugins/{name}/skills")).Select(Path.GetFileName);
        Assert.Equal([name], skills);
        var meta = Repository.FrontMatter($"plugins/{name}/skills/{name}/SKILL.md");
        Assert.Equal(name, meta.GetValueOrDefault("name"));
        Assert.True(meta.GetValueOrDefault("description", "").Length >= 40, $"{name}: skill needs a real description");
    }

    [Fact]
    public void CodexMarketplaceListsThePlugins()
    {
        var market = Repository.Json(".agents/plugins/marketplace.json");
        Assert.Equal(Marketplace, (string?)market["name"]);
        var entries = Entries(".agents/plugins/marketplace.json");
        Assert.Equal(CodexPlugins, entries.Select(e => (string?)e!["name"]));
        foreach (var entry in entries)
        {
            var name = (string)entry!["name"]!;
            var source = CodexUrls.TryGetValue(name, out var url)
                ? $$"""{"source":"url","url":"{{url}}"}""" : $$"""{"source":"local","path":"./codex/{{name}}"}""";
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source), entry["source"]), $"{name}: source must be {source}");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"installation":"AVAILABLE","authentication":"ON_INSTALL"}"""), entry["policy"]), $"{name}: policy");
            Assert.False(string.IsNullOrEmpty((string?)entry["category"]));
        }
        Assert.Equal(CodexPlugins.Except(CodexUrls.Keys).Order(), Directory.GetDirectories(Repository.PathOf("codex")).Select(Path.GetFileName).Order());
    }

    public static TheoryData<string> LocalCodexNames => new(CodexPlugins.Except(CodexUrls.Keys));

    [Theory]
    [MemberData(nameof(LocalCodexNames))]
    public void CodexPluginMatchesItsClaudeTwin(string name)
    {
        var manifest = Repository.Json($"codex/{name}/.codex-plugin/plugin.json");
        var version = Version(name);
        Assert.Equal(name, (string?)manifest["name"]);
        Assert.Equal(version, (string?)manifest["version"]);
        Assert.Equal("./.mcp.json", (string?)manifest["mcpServers"]);
        Assert.Equal("./skills/", (string?)manifest["skills"]);
        Assert.False(string.IsNullOrEmpty((string?)manifest["interface"]?["displayName"]));
        foreach (var (field, size) in new[] { ("composerIcon", 64), ("logo", 512) })
        {
            var relative = (string?)manifest["interface"]?[field] ?? "";
            Assert.StartsWith("./assets/", relative);
            Assert.Equal((size, size), Repository.PngSize($"codex/{name}/{relative[2..]}"));
        }
        var servers = Repository.Json($"codex/{name}/.mcp.json")["mcpServers"]!;
        var skill = $"codex/{name}/skills/{name}/SKILL.md";
        var meta = Repository.FrontMatter(skill);
        Assert.Equal(name, meta.GetValueOrDefault("name"));
        Assert.True(meta.GetValueOrDefault("description", "").Length >= 40);
        if (name == "nexus-relay")
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RelayCodex), servers), "relay: exactly the local daemon");
            // The edit guard: the relay's native engine at the stable path its installer keeps, synchronously. Codex
            // reads no variable in a command: cmd.exe expands %USERPROFILE%, whether Codex runs the line through a
            // shell or splits it. NexusRelay's installer recognises it (NexusRelay.exe --edit-guard, apply_patch).
            const string guard = """cmd.exe /d /c \"%USERPROFILE%\\.nexus\\nexus-relay\\bin\\NexusRelay.exe\" --edit-guard codex""";
            var expected = $$$"""
                {"hooks":{
                  "PreToolUse":[{"matcher":"apply_patch","hooks":[{"type":"command","command":"{{{guard}}}","timeout":15,"async":false}]}],
                  "PostToolUse":[{"matcher":"apply_patch","hooks":[{"type":"command","command":"{{{guard}}} --finish-tool","timeout":15,"async":false}]}],
                  "Stop":[{"hooks":[{"type":"command","command":"{{{guard}}} --finish-session","timeout":15,"async":false}]}]}}
                """;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), manifest["hooks"]), "relay: native edit guard from its stable path");
            return;
        }
        if (Daemons.ContainsKey(name))
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Daemons[name].Codex), servers), $"{name}: exactly the local daemon");
            Assert.Equal(Repository.Text($"plugins/{name}/skills/{name}/SKILL.md").ReplaceLineEndings(), Repository.Text(skill).ReplaceLineEndings());
            return;
        }
        var server = Assert.Single(servers.AsObject());
        Assert.Equal(name, server.Key);
        Assert.Equal("uvx", (string?)server.Value!["command"]);
        Assert.Equal(["--python", "3.14", $"{PyPI[name]}=={version}"], server.Value["args"]!.AsArray().Select(a => (string?)a));
        if (name == "nexus-browser") Assert.Equal("Codex", (string?)server.Value["env"]?["NEXUS_BROWSER_AGENT"]);
        // The same guide for both apps.
        Assert.Equal(Repository.Text($"plugins/{name}/skills/{name}/SKILL.md").ReplaceLineEndings(), Repository.Text(skill).ReplaceLineEndings());
    }

    [Fact]
    public void NativeProductsNeverGoThroughPython()
    {
        // No Python in the repository's own code, and the products already native (NexusAndroid, NexusRelay, NexusAGYmail, NexusKeypad) are
        // installed by their own get.ps1, never by uvx or PyPI.
        Assert.DoesNotContain(Repository.TrackedFiles(), f => f.EndsWith(".py", StringComparison.Ordinal));
        foreach (var file in new[] { "get-nexus.ps1", "app/src/Nexus.App/Text.cs", "README.md", "INSTALL-AI.md" })
        {
            var text = Repository.Text(file);
            Assert.DoesNotMatch(new Regex(@"nexus-(?:agymail|android)@(?:latest|\d)|nexus-(?:agymail|android)(?:>=|==)|nexus-(?:agymail|android)\s+(?:auth|setup)|uvx\b[^\n]*nexus-(?:agymail|android)|nexus-keypad==|nexus-relay==|--from nexus-relay|pypi\.org/project/nexus-(?:agymail|android|relay|keypad)", RegexOptions.IgnoreCase), text);
        }
        foreach (var product in new[] { "NexusAndroid", "NexusRelay", "NexusAGYmail", "NexusKeypad" })
            Assert.Contains($"Invoke-Installer '{product}' '{product}' $work", Repository.Text("get-nexus.ps1"));
    }

    [Fact]
    public void CatalogNamesNoLocalPathTokenOrPerson()
    {
        var problems = new List<string>();
        var files = Repository.TrackedFiles().Where(f => f.StartsWith("plugins/", StringComparison.Ordinal) || f.StartsWith("codex/", StringComparison.Ordinal) ||
            f.StartsWith(".claude-plugin/", StringComparison.Ordinal) || f.StartsWith(".agents/", StringComparison.Ordinal))
            .Where(f => Path.GetExtension(f) is ".json" or ".md" or ".ts" or ".tsx" or ".toml");
        foreach (var file in files)
        {
            var text = Repository.Text(file);
            if (Regex.Match(text, @"[A-Za-z]:\\\\?Users\\|/home/|OneDrive", RegexOptions.IgnoreCase) is { Success: true } path) problems.Add($"{file}: local path {path.Value}");
            // A token is only ever read from the user variable, never written.
            if (Regex.Match(text, @"Bearer (?!\$\{NEXUS_MCP_TOKEN\})\S+") is { Success: true } token) problems.Add($"{file}: token {token.Value}");
            if (file.EndsWith("SKILL.md", StringComparison.Ordinal) && Regex.IsMatch(text, @"\bJulien\b")) problems.Add($"{file}: names the author where a third-party agent reads it");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
