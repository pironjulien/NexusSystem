using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>
/// The two catalogs (Claude Code: .claude-plugin, Codex: .agents/plugins). Each plugin lives in one folder, `plugin/` of
/// its product's repository, shared by the three AI apps; the catalogs point to it at the product's release tag
/// (git-subdir), so a product release never needs a copy here. Each plugin reaches its
/// product's native Windows engine, either the local daemon (HTTP, token read from the user variable NEXUS_MCP_TOKEN)
/// or the native executable at the stable path its installer keeps. Nothing starts Python, uv or a PyPI package.
/// </summary>
public class CatalogTests
{
    const string Marketplace = "nexus-plugins";
    static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$");
    static readonly Regex KebabCase = new(@"^[a-z0-9]+(-[a-z0-9]+)*$");
    static readonly string[] Plugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay", "nexus-keypad"];
    static readonly string[] CodexPlugins = ["nexus-android", "nexus-windows", "nexus-browser", "nexus-relay", "nexus-keypad"];
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
        // nexus-keypad: hooks only, no MCP server.
    };

    static JsonArray Entries(string marketplace) => Repository.Json(marketplace)["plugins"]!.AsArray();

    const string ClaudeCatalog = ".claude-plugin/marketplace.json";
    const string CodexCatalog = ".agents/plugins/marketplace.json";

    /// <summary>Folder of a plugin as the AI app installs it: inside this repository (relative source), or its product
    /// repository's folder at the pinned release (git-subdir), fetched once into the build cache.</summary>
    static string Folder(string catalog, string name) =>
        PluginSources.Folder(Entries(catalog).Single(e => (string?)e!["name"] == name)!["source"]!);

    static JsonNode Json(string folder, string relative) => JsonNode.Parse(File.ReadAllText(Under(folder, relative)))
        ?? throw new InvalidDataException($"{relative}: empty JSON");

    static string Under(string folder, string relative) =>
        Path.Combine(folder, (relative.StartsWith("./", StringComparison.Ordinal) ? relative[2..] : relative).Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void ClaudeMarketplaceListsEveryPluginFolder()
    {
        var market = Repository.Json(".claude-plugin/marketplace.json");
        Assert.Equal(Marketplace, (string?)market["name"]);
        Assert.False(string.IsNullOrEmpty((string?)market["owner"]?["name"]));
        Assert.Matches(SemVer, (string?)market["metadata"]?["version"] ?? "");
        Assert.Equal(Plugins, Entries(ClaudeCatalog).Select(e => (string?)e!["name"]));
        foreach (var entry in Entries(ClaudeCatalog)) PluginSources.AssertPinned(entry!);
        Assert.DoesNotContain(Repository.TrackedFiles(), f => f.StartsWith("plugins/", StringComparison.Ordinal));
        Assert.DoesNotContain(Repository.TrackedFiles(), f => f.StartsWith("codex/", StringComparison.Ordinal));
    }

    public static TheoryData<string> PluginNames => new(Plugins);

    public static TheoryData<string> CodexNames => new(CodexPlugins);

    [Theory]
    [MemberData(nameof(PluginNames))]
    public void ClaudePluginManifestMatchesItsEntry(string name)
    {
        var entry = Entries(ClaudeCatalog).Single(e => (string?)e!["name"] == name)!;
        var folder = Folder(ClaudeCatalog, name);
        var manifest = Json(folder, ".claude-plugin/plugin.json");
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
        Assert.True(Repository.PngSizeOf(Under(folder, (string?)manifest["icon"] ?? "")) is var (w, h) && w == h && w is >= 512 and <= 2048,
            $"{name}: icon must be a square PNG of 512 to 2048 px");
        Assert.True(File.Exists(Path.Combine(folder, "LICENSE")), $"{name}: LICENSE missing");
    }

    [Theory]
    [MemberData(nameof(PluginNames))]
    public void ClaudePluginReachesTheNativeEngine(string name)
    {
        var folder = Folder(ClaudeCatalog, name);
        var mcp = Path.Combine(folder, ".mcp.json");
        if (ClaudeServers[name] is not { } expected)
        {
            Assert.False(File.Exists(mcp), $"{name}: no MCP server expected");
            return;
        }
        var servers = Json(folder, ".mcp.json")["mcpServers"];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), servers), $"{name}: .mcp.json must be exactly {expected}");
    }

    [Fact]
    public void WindowsPluginUsesTheStablePathOfItsInstaller()
    {
        // The path NexusWindows' installer keeps (an immutable versioned copy behind it), the same for every profile.
        var server = Json(Folder(ClaudeCatalog, "nexus-windows"), ".mcp.json")["mcpServers"]!["nexus-windows"]!;
        Assert.Equal(StableWindows, (string?)server["command"]);
    }

    [Fact]
    public void KeypadPluginCallsOnlyItsNativeHelper()
    {
        var folder = Folder(ClaudeCatalog, "nexus-keypad");
        var hooks = Json(folder, "hooks/hooks.json");
        Assert.Equal(["./register.tsx"], hooks["modules"]!.AsArray().Select(m => (string?)m));
        foreach (var module in new[] { "register.tsx", "quota.ts" })
            Assert.True(File.Exists(Path.Combine(folder, "hooks", module)), $"keypad module {module} missing");
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
        var folder = Folder(ClaudeCatalog, name);
        var skills = Directory.GetDirectories(Path.Combine(folder, "skills")).Select(Path.GetFileName);
        Assert.Equal([name], skills);
        var meta = Repository.FrontMatterOf(Path.Combine(folder, "skills", name, "SKILL.md"));
        Assert.Equal(name, meta.GetValueOrDefault("name"));
        Assert.True(meta.GetValueOrDefault("description", "").Length >= 40, $"{name}: skill needs a real description");
    }

    [Fact]
    public void CodexMarketplaceListsThePlugins()
    {
        var market = Repository.Json(CodexCatalog);
        Assert.Equal(Marketplace, (string?)market["name"]);
        var entries = Entries(CodexCatalog);
        Assert.Equal(CodexPlugins, entries.Select(e => (string?)e!["name"]));
        foreach (var entry in entries)
        {
            var name = (string?)entry!["name"];
            PluginSources.AssertPinned(entry);
            // The same product release as Claude Code's entry.
            var claude = Entries(ClaudeCatalog).Single(e => (string?)e!["name"] == name)!["source"]!;
            Assert.Equal(PluginSources.Describe(claude), PluginSources.Describe(entry["source"]!));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"installation":"AVAILABLE","authentication":"ON_INSTALL"}"""), entry["policy"]), $"{name}: policy");
            Assert.False(string.IsNullOrEmpty((string?)entry["category"]));
        }
    }

    [Theory]
    [MemberData(nameof(CodexNames))]
    public void CodexPluginMatchesItsClaudeTwin(string name)
    {
        var folder = Folder(CodexCatalog, name);
        var claudeFolder = Folder(ClaudeCatalog, name);
        var manifest = Json(folder, ".codex-plugin/plugin.json");
        var claude = Json(claudeFolder, ".claude-plugin/plugin.json");
        Assert.Equal(name, (string?)manifest["name"]);
        Assert.Equal((string?)claude["version"], (string?)manifest["version"]);
        Assert.Equal((string?)claude["description"], (string?)manifest["description"]);
        var skillsFolder = (string?)manifest["skills"] ?? "";
        Assert.StartsWith("./", skillsFolder);
        if (name is not ("nexus-relay" or "nexus-keypad")) Assert.Null(manifest["hooks"]);
        Assert.False(string.IsNullOrEmpty((string?)manifest["interface"]?["displayName"]));
        foreach (var (field, size) in new[] { ("composerIcon", 64), ("logo", 512) })
        {
            var relative = (string?)manifest["interface"]?[field] ?? "";
            Assert.StartsWith("./assets/", relative);
            Assert.Equal((size, size), Repository.PngSizeOf(Under(folder, relative)));
        }
        if (CodexServers.TryGetValue(name, out var expected))
        {
            var mcpFile = (string?)manifest["mcpServers"] ?? "";
            Assert.StartsWith("./", mcpFile);
            var servers = Json(folder, mcpFile)["mcpServers"];
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), servers), $"{name}: .mcp.json must be exactly {expected}");
        }
        else Assert.Null(manifest["mcpServers"]);
        var skill = Path.Combine(Under(folder, skillsFolder), name, "SKILL.md");
        var meta = Repository.FrontMatterOf(skill);
        Assert.Equal(name, meta.GetValueOrDefault("name"));
        Assert.True(meta.GetValueOrDefault("description", "").Length >= 40);
        // The same guide for both apps, except the relay's (Codex answers, Claude hosts).
        if (name != "nexus-relay")
            Assert.Equal(File.ReadAllText(Path.Combine(claudeFolder, "skills", name, "SKILL.md")).ReplaceLineEndings(), File.ReadAllText(skill).ReplaceLineEndings());
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
        var catalogs = Repository.TrackedFiles().Where(f => f.StartsWith(".claude-plugin/", StringComparison.Ordinal) || f.StartsWith(".agents/", StringComparison.Ordinal))
            .Select(Repository.PathOf);
        var git = $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}";
        var plugins = Plugins.Select(n => Folder(ClaudeCatalog, n)).Concat(CodexPlugins.Select(n => Folder(CodexCatalog, n))).Distinct()
            .SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories).Where(x => !x.Contains(git, StringComparison.Ordinal)));
        var files = catalogs.Concat(plugins).Where(f => Path.GetExtension(f) is ".json" or ".md" or ".ts" or ".tsx");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
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
        var folder = Folder(CodexCatalog, "nexus-windows");
        var mcpFile = (string?)Json(folder, ".codex-plugin/plugin.json")["mcpServers"] ?? "";
        var args = Json(folder, mcpFile)["mcpServers"]!["nexus-windows"]!["args"]!.AsArray().Select(a => (string?)a);
        Assert.Equal("/d /c cd /d %USERPROFILE%\\.nexus\\nexus-windows && .\\NexusWindows.exe --mcp --agent codex", string.Join(" ", args));
    }

    [Fact]
    public void ClaudeRelayHooksUseTheStableEngine()
    {
        // Claude Code runs hooks with bash: the engine NexusRelay's installer keeps at ~/.nexus/nexus-relay/bin. The
        // edit guard on file edits, the release after the tool and at the end, the notice of unread messages.
        var events = Json(Folder(ClaudeCatalog, "nexus-relay"), "hooks/hooks.json")["hooks"]!.AsObject();
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


    [Fact]
    public void NoDownloadedCodeRunsInMemory()
    {
        // Microsoft Defender rightly flags downloaded code run in memory (Trojan:Win32/Commando.A!ml): installers are
        // always files of a release, checked, then run with -File. Nowhere: irm | iex, Invoke-Expression on a download,
        // [scriptblock]::Create of a download, -EncodedCommand.
        var forbidden = new Regex(@"\birm\b|\biwr\b[^\n]*\|\s*iex|\|\s*iex\b|Invoke-Expression|scriptblock\]::Create\(\(\s*(?:irm|iwr|Invoke-RestMethod|Invoke-WebRequest)|-EncodedCommand",
            RegexOptions.IgnoreCase);
        var found = Repository.TrackedFiles()
            .Where(f => f != "CHANGELOG.md" && !f.StartsWith("tests/NexusSystem.Tests/", StringComparison.Ordinal) &&
                Path.GetExtension(f) is ".ps1" or ".md" or ".cs" or ".yml" or ".json")
            .SelectMany(f => Repository.Text(f).Split('\n').Select((line, i) => (f, i, line)))
            .Where(x => forbidden.IsMatch(x.line) && !x.line.Contains("Assert.DoesNotContain", StringComparison.Ordinal))
            .Select(x => $"{x.f}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(found.Count == 0, "Downloaded code run in memory:\n" + string.Join("\n", found));
    }


    [Fact]
    public void CodexRelayUsesTheNativeEditGuard()
    {
        var manifest = Json(Folder(CodexCatalog, "nexus-relay"), ".codex-plugin/plugin.json");
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
    }

    [Fact]
    public void CodexKeypadHooksCallOnlyItsNativeHelper()
    {
        // Codex's lifecycle events, synchronous, to the helper NexusKeypad installs at a stable place; cmd.exe expands
        // %LOCALAPPDATA%, so the catalog copy needs nothing of this PC.
        var events = Json(Folder(CodexCatalog, "nexus-keypad"), ".codex-plugin/plugin.json")["hooks"]!["hooks"]!.AsObject();
        Assert.Equal(["SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PostToolUse", "SubagentStart",
            "SubagentStop", "Stop", "Interrupt", "SessionEnd"], events.Select(e => e.Key));
        foreach (var (eventName, groups) in events)
        {
            var hook = Assert.Single(Assert.Single(groups!.AsArray())!["hooks"]!.AsArray())!;
            Assert.Equal($"cmd.exe /d /c \"%LOCALAPPDATA%\\NexusKeypad\\hook\\nexuskeypad-hook.exe\" codex {eventName}", (string?)hook["command"]);
            Assert.False((bool?)hook["async"]);
        }
    }
}
