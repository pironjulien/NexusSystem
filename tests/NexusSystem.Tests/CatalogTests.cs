using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>
/// The two catalogs (Claude Code: .claude-plugin + plugins/, Codex: .agents/plugins + codex/): each plugin reaches its
/// product's native Windows engine, either the local daemon (HTTP, token read from the user variable NEXUS_MCP_TOKEN)
/// or the native executable at the stable path its installer keeps. Nothing starts Python, uv or a PyPI package.
/// </summary>
public class CatalogTests
{
    const string Marketplace = "nexus-plugins";
    static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$");
    static readonly Regex KebabCase = new(@"^[a-z0-9]+(-[a-z0-9]+)*$");
    static readonly string[] Plugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay", "nexus-keypad"];
    static readonly string[] CodexPlugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay"];
    const string StableWindows = @"${USERPROFILE}\.nexus\nexus-windows\NexusWindows.exe";

    /// <summary>Exactly what each Claude Code plugin declares (the private catalog's native forms, public).</summary>
    static readonly Dictionary<string, string?> ClaudeServers = new()
    {
        ["nexus-relay"] = """{"nexus-relay":{"type":"http","url":"http://127.0.0.1:16181/mcp?agent=claude","headers":{"Authorization":"Bearer ${NEXUS_MCP_TOKEN}"}}}""",
        ["nexus-android"] = """{"nexus-android":{"type":"http","url":"http://127.0.0.1:16180/mcp","headers":{"Authorization":"Bearer ${NEXUS_MCP_TOKEN}"}}}""",
        ["nexus-browser"] = """{"nexus-browser":{"type":"http","url":"http://127.0.0.1:16182/mcp?agent=claude","headers":{"Authorization":"Bearer ${NEXUS_MCP_TOKEN}"}}}""",
        ["nexus-windows"] = """{"nexus-windows":{"type":"stdio","command":"${USERPROFILE}\\.nexus\\nexus-windows\\NexusWindows.exe","args":["--mcp","--agent","claude"]}}""",
        ["nexus-keypad"] = null,  // hooks and modules only, no MCP server
    };

    /// <summary>Codex reads no variable in a plugin's command: the native engine is reached from its stable folder.</summary>
    static readonly Dictionary<string, string> CodexServers = new()
    {
        ["nexus-relay"] = """{"nexus-relay":{"url":"http://127.0.0.1:16181/mcp?agent=codex","bearer_token_env_var":"NEXUS_MCP_TOKEN","startup_timeout_sec":60}}""",
        ["nexus-android"] = """{"nexus-android":{"url":"http://127.0.0.1:16180/mcp","bearer_token_env_var":"NEXUS_MCP_TOKEN","startup_timeout_sec":60}}""",
        ["nexus-browser"] = """{"nexus-browser":{"url":"http://127.0.0.1:16182/mcp?agent=codex","bearer_token_env_var":"NEXUS_MCP_TOKEN","startup_timeout_sec":60}}""",
        ["nexus-windows"] = """{"nexus-windows":{"command":"cmd.exe","args":["/d","/c","cd","/d","%USERPROFILE%\\.nexus\\nexus-windows","&&",".\\NexusWindows.exe","--mcp","--agent","codex"],"startup_timeout_sec":120}}""",
    };

    static JsonArray Entries(string marketplace) => Repository.Json(marketplace)["plugins"]!.AsArray();

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

    public static TheoryData<string> PluginNames => new(Plugins);

    public static TheoryData<string> CodexNames => new(CodexPlugins);

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
        Assert.Equal((string?)manifest["description"], (string?)entry["description"]);
        foreach (var field in new[] { "description", "license", "repository", "homepage" })
        {
            Assert.False(string.IsNullOrEmpty((string?)manifest[field]), $"{name}: plugin.json {field} missing");
            Assert.False(string.IsNullOrEmpty((string?)entry[field]), $"{name}: marketplace {field} missing");
        }
        // Says what the user installs first: the product, by NexusSystem.
        Assert.Contains("NexusSystem", (string?)manifest["description"]);
        Assert.Equal("./.claude-plugin/icon.png", (string?)manifest["icon"]);
        Assert.True(Repository.PngSize($"plugins/{name}/.claude-plugin/icon.png") is var (w, h) && w == h && w is >= 512 and <= 2048,
            $"{name}: icon must be a square PNG of 512 to 2048 px");
        Assert.True(File.Exists(Repository.PathOf($"plugins/{name}/LICENSE")), $"{name}: LICENSE missing");
        Assert.True(File.Exists(Repository.PathOf($"plugins/{name}/README.md")), $"{name}: README.md missing");
    }

    [Theory]
    [MemberData(nameof(PluginNames))]
    public void ClaudePluginReachesTheNativeEngine(string name)
    {
        var mcp = Repository.PathOf($"plugins/{name}/.mcp.json");
        if (ClaudeServers[name] is not { } expected)
        {
            Assert.False(File.Exists(mcp), $"{name}: no MCP server expected");
            return;
        }
        var servers = Repository.Json($"plugins/{name}/.mcp.json")["mcpServers"];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), servers), $"{name}: .mcp.json must be exactly {expected}");
    }

    [Fact]
    public void WindowsPluginUsesTheStablePathOfItsInstaller()
    {
        // The path NexusWindows' installer keeps (an immutable versioned copy behind it), the same for every profile.
        var server = Repository.Json("plugins/nexus-windows/.mcp.json")["mcpServers"]!["nexus-windows"]!;
        Assert.Equal(StableWindows, (string?)server["command"]);
    }

    [Fact]
    public void KeypadPluginCallsOnlyItsNativeHelper()
    {
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
    public void CodexMarketplaceListsTheLocalPlugins()
    {
        var market = Repository.Json(".agents/plugins/marketplace.json");
        Assert.Equal(Marketplace, (string?)market["name"]);
        var entries = Entries(".agents/plugins/marketplace.json");
        Assert.Equal(CodexPlugins, entries.Select(e => (string?)e!["name"]));
        foreach (var entry in entries)
        {
            var name = (string?)entry!["name"];
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse($$"""{"source":"local","path":"./codex/{{name}}"}"""), entry["source"]), $"{name}: local source");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"installation":"AVAILABLE","authentication":"ON_INSTALL"}"""), entry["policy"]), $"{name}: policy");
            Assert.False(string.IsNullOrEmpty((string?)entry["category"]));
        }
        Assert.Equal(CodexPlugins.Order(), Directory.GetDirectories(Repository.PathOf("codex")).Select(Path.GetFileName).Order());
    }

    [Theory]
    [MemberData(nameof(CodexNames))]
    public void CodexPluginMatchesItsClaudeTwin(string name)
    {
        var manifest = Repository.Json($"codex/{name}/.codex-plugin/plugin.json");
        var claude = Repository.Json($"plugins/{name}/.claude-plugin/plugin.json");
        Assert.Equal(name, (string?)manifest["name"]);
        Assert.Equal((string?)claude["version"], (string?)manifest["version"]);
        Assert.Equal((string?)claude["description"], (string?)manifest["description"]);
        Assert.Equal("./.mcp.json", (string?)manifest["mcpServers"]);
        Assert.Equal("./skills/", (string?)manifest["skills"]);
        Assert.Null(manifest["hooks"]);  // the relay's edit guard is declared by NexusRelay's installer, with its own path
        Assert.False(string.IsNullOrEmpty((string?)manifest["interface"]?["displayName"]));
        foreach (var (field, size) in new[] { ("composerIcon", 64), ("logo", 512) })
        {
            var relative = (string?)manifest["interface"]?[field] ?? "";
            Assert.StartsWith("./assets/", relative);
            Assert.Equal((size, size), Repository.PngSize($"codex/{name}/{relative[2..]}"));
        }
        var servers = Repository.Json($"codex/{name}/.mcp.json")["mcpServers"];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(CodexServers[name]), servers), $"{name}: .mcp.json must be exactly {CodexServers[name]}");
        var skill = $"codex/{name}/skills/{name}/SKILL.md";
        var meta = Repository.FrontMatter(skill);
        Assert.Equal(name, meta.GetValueOrDefault("name"));
        Assert.True(meta.GetValueOrDefault("description", "").Length >= 40);
        // The same guide for both apps, except the relay's (Codex answers, Claude hosts).
        if (name != "nexus-relay")
            Assert.Equal(Repository.Text($"plugins/{name}/skills/{name}/SKILL.md").ReplaceLineEndings(), Repository.Text(skill).ReplaceLineEndings());
    }

    /// <summary>
    /// What the agents and users read or run: catalogs, plugins, installers, app, docs, CI. CHANGELOG.md keeps
    /// history; app/tests holds fixtures of the processes and configurations former installers left on a PC, which
    /// the app and the pack still recognise (to replace them), never start.
    /// </summary>
    static IEnumerable<string> PublishedText() => Repository.TrackedFiles().Where(f =>
        f != "CHANGELOG.md" && !f.StartsWith("tests/NexusSystem.Tests/", StringComparison.Ordinal) && !f.StartsWith("app/tests/", StringComparison.Ordinal) &&
        Path.GetExtension(f) is ".json" or ".md" or ".ps1" or ".psd1" or ".yml" or ".toml" or ".cs" or ".ts" or ".tsx" or ".xaml");

    [Fact]
    public void NothingStartsPythonUvOrPyPI()
    {
        var files = Repository.TrackedFiles();
        Assert.DoesNotContain(files, f => f.EndsWith(".py", StringComparison.Ordinal) || Path.GetFileName(f) is "pyproject.toml" or "uv.lock");
        // Starting uv, uvx, Python or pip, downloading uv, or reaching PyPI, in any form a script, a manifest, the app
        // or a document would use.
        var forbidden = new Regex(@"\buvx(?:\.exe)?""?\s+[-a-z]|""command""\s*:\s*""uvx|Get-Uvx|\buv\s+(?:run|sync|lock|tool|pip|venv|cache)\b|" +
            @"astral\.sh|astral-sh|pypi\.org|setup-uv|setup-python|\bpip3?\s+install|\bpython(?:\.exe|3)?""?\s+(?:-[a-zA-Z]|\S+\.py\b)",
            RegexOptions.IgnoreCase);
        var found = PublishedText()
            .SelectMany(f => Repository.Text(f).Split('\n').Select((line, i) => (f, i, line)))
            .Where(x => forbidden.IsMatch(x.line))
            .Select(x => $"{x.f}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(found.Count == 0, "Python, uv or PyPI still referenced:\n" + string.Join("\n", found));
    }

    [Fact]
    public void CatalogNamesNoLocalPathTokenOrPerson()
    {
        var problems = new List<string>();
        var files = Repository.TrackedFiles().Where(f => f.StartsWith("plugins/", StringComparison.Ordinal) || f.StartsWith("codex/", StringComparison.Ordinal) ||
            f.StartsWith(".claude-plugin/", StringComparison.Ordinal) || f.StartsWith(".agents/", StringComparison.Ordinal))
            .Where(f => Path.GetExtension(f) is ".json" or ".md" or ".ts" or ".tsx");
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

    [Fact]
    public void CodexWindowsLauncherIsTheOneTheAppRecognises()
    {
        // The NexusSystem app identifies Codex's NexusWindows connection through this exact launcher (Servers.cs).
        var args = Repository.Json("codex/nexus-windows/.mcp.json")["mcpServers"]!["nexus-windows"]!["args"]!.AsArray().Select(a => (string?)a);
        Assert.Equal("/d /c cd /d %USERPROFILE%\\.nexus\\nexus-windows && .\\NexusWindows.exe --mcp --agent codex", string.Join(" ", args));
    }
}
