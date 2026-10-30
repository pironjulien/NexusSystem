using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusSystem.Tests;

/// <summary>
/// After a real run of get-nexus.ps1 (CI's pack job, or a disposable PC): every Nexus MCP server that each AI app now
/// declares is reached exactly as that app would reach it (its own configuration: command and arguments, or URL and
/// token), then answers initialize and tools/list. Runs only when NEXUS_PACK_CHECK_AIS names the AI apps to check
/// (claude-code, claude-desktop, codex, antigravity); NEXUS_PACK_CHECK_PRODUCTS narrows the products (default
/// windows,android,browser). Never on a PC by accident: it reads the real profile.
/// </summary>
public class InstallTests
{
    static readonly Dictionary<string, string> Servers = new()
    {
        ["windows"] = "nexus-windows", ["android"] = "nexus-android", ["browser"] = "nexus-browser",
        ["relay"] = "nexus-relay", ["agymail"] = "nexus-agymail",
    };
    // Current name first, then the former one of an installation not migrated yet.
    static readonly Dictionary<string, string[]> AntigravityPlugins = new()
    {
        ["windows"] = ["nexus-windows", "nexus-computer"], ["android"] = ["nexus-android"], ["browser"] = ["nexus-browser"],
        ["relay"] = ["nexus-relay"], ["agymail"] = ["nexus-agymail"],
    };
    // The AI apps a product serves (get-nexus.ps1): Claude Desktop's chat is not a relay client, AGYmail is for Antigravity.
    static readonly Dictionary<string, string[]> NotServed = new() { ["relay"] = ["claude-desktop"], ["agymail"] = ["claude-code", "claude-desktop", "codex"] };
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public async Task EveryDeclaredServerAnswers()
    {
        var ais = Environment.GetEnvironmentVariable("NEXUS_PACK_CHECK_AIS");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(ais), "Set NEXUS_PACK_CHECK_AIS after a real pack run (CI pack job).");
        var products = (Environment.GetEnvironmentVariable("NEXUS_PACK_CHECK_PRODUCTS") is { Length: > 0 } list ? list : "windows,android,browser").Split(',');
        var failures = new List<string>();
        foreach (var ai in ais!.Split(','))
        {
            foreach (var product in products)
            {
                if (NotServed.TryGetValue(product, out var notServed) && notServed.Contains(ai)) continue;
                var label = $"{ai}/{Servers[product]}";
                try
                {
                    var declared = Declared(ai, product);
                    if (declared is null) { failures.Add($"{label}: not declared"); continue; }
                    var (name, version, tools) = await Reach(declared);
                    var catalogManifest = Repository.PathOf($"plugins/{Servers[product]}/.claude-plugin/plugin.json");
                    if (File.Exists(catalogManifest) && (string?)ReadJson(catalogManifest)?["version"] is { } expected && version != expected)
                    {
                        failures.Add($"{label}: initialize reports version {version}, catalog requires {expected}");
                        continue;
                    }
                    TestContext.Current.TestOutputHelper?.WriteLine($"OK {label}: {name} {version}, {tools} tools");
                }
                catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    failures.Add($"{label}: {error.Message}");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    static JsonNode? ReadJson(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;

    /// <summary>The server declaration the AI app reads, or null when it declares none.</summary>
    static JsonObject? Declared(string ai, string product)
    {
        var server = Servers[product];
        switch (ai)
        {
            case "claude-code":
            {
                var cache = Path.Combine(Home, ".claude", "plugins", "cache", "nexus-plugins", server);
                var newest = Directory.Exists(cache)
                    ? Directory.GetDirectories(cache).OrderBy(Directory.GetLastWriteTimeUtc).LastOrDefault(d => File.Exists(Path.Combine(d, ".mcp.json")))
                    : null;
                // Declared in settings.json only (no claude command): with a local catalog (tests), that folder's plugin.
                var settings = ReadJson(Path.Combine(Home, ".claude", "settings.json"));
                var source = settings?["extraKnownMarketplaces"]?["nexus-plugins"]?["source"];
                if (newest is null && (bool?)settings?["enabledPlugins"]?[$"{server}@nexus-plugins"] == true && (string?)source?["source"] == "directory")
                    newest = Path.Combine((string)source!["path"]!, "plugins", server);
                return newest is null ? null : ReadJson(Path.Combine(newest, ".mcp.json"))?["mcpServers"]?[server]?.DeepClone().AsObject();
            }
            case "claude-desktop":
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folders = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude") }
                    .Concat(Directory.Exists(Path.Combine(local, "Packages")) ? Directory.GetDirectories(Path.Combine(local, "Packages"), "*Claude_*").Select(p => Path.Combine(p, @"LocalCache\Roaming\Claude")) : [])
                    .Concat(Directory.Exists(Path.Combine(local, "Claude-Data")) ? Directory.GetDirectories(Path.Combine(local, "Claude-Data")) : []);
                foreach (var folder in folders.Where(f => Directory.Exists(f) && Directory.GetFiles(f, ".user-data-left.*").Length == 0))
                    if (ReadJson(Path.Combine(folder, "claude_desktop_config.json"))?["mcpServers"]?[server] is JsonObject found) return found.DeepClone().AsObject();
                return null;
            }
            case "codex":
            {
                var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } h ? h : Path.Combine(Home, ".codex");
                var config = Path.Combine(codexHome, "config.toml");
                if (!File.Exists(config)) return null;
                var text = File.ReadAllText(config);
                if (Regex.IsMatch(text, $@"(?m)^\s*\[mcp_servers\.""?{Regex.Escape(server)}""?\]"))
                    throw new InvalidDataException("a direct MCP declaration remains beside the plugin");
                var catalogs = Regex.Matches(text, $@"(?m)^\s*\[plugins\.""{Regex.Escape(server)}@([^""]+)""\]\s*\r?\n(?:(?!\[).*\r?\n)*?\s*enabled\s*=\s*true")
                    .Select(m => m.Groups[1].Value).ToList();
                if (catalogs.Count != 1) throw new InvalidDataException($"expected one enabled plugin, found {catalogs.Count}");
                var cache = Path.Combine(codexHome, "plugins", "cache", catalogs[0], server);
                foreach (var root in Directory.Exists(cache) ? Directory.GetDirectories(cache).OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray() : [])
                {
                    var manifest = ReadJson(Path.Combine(root, ".codex-plugin", "plugin.json"));
                    if ((string?)manifest?["name"] == server && (string?)manifest?["skills"] == "./skills/" && File.Exists(Path.Combine(root, "skills", server, "SKILL.md")) &&
                        ReadJson(Path.Combine(root, ".mcp.json"))?["mcpServers"]?[server] is JsonObject found) return found.DeepClone().AsObject();
                }
                throw new InvalidDataException("installed plugin with its MCP declaration and guide not found");
            }
            case "antigravity":
            {
                var plugins = Path.Combine(Home, ".gemini", "config", "plugins");
                var plugin = AntigravityPlugins[product].Select(n => Path.Combine(plugins, n)).FirstOrDefault(p => File.Exists(Path.Combine(p, "plugin.json")));
                return plugin is null ? null : (ReadJson(Path.Combine(plugin, "mcp_config.json"))?["mcpServers"] as JsonObject)?.Select(s => s.Value).OfType<JsonObject>().FirstOrDefault()?.DeepClone().AsObject();
            }
            default:
                throw new InvalidOperationException($"unknown AI app {ai}");
        }
    }

    /// <summary>What Claude Code does for a plugin's .mcp.json, and Windows for cmd.exe: ${VAR}, ${VAR:-default}, %VAR%.</summary>
    static string Expand(string text) => Environment.ExpandEnvironmentVariables(Regex.Replace(text, @"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}",
        m => Environment.GetEnvironmentVariable(m.Groups[1].Value) is { Length: > 0 } value ? value : m.Groups[2].Value));

    static string? UserVariable(string name) => Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    static async Task<(string Name, string Version, int Tools)> Reach(JsonObject server)
    {
        var url = (string?)server["url"] ?? (string?)server["serverUrl"] ?? (string?)server["httpUrl"];
        var (init, tools) = url is not null ? await OverHttp(url, server) : await OverStdio(server);
        var seen = (string?)init["instructions"] + tools.ToJsonString();
        // What every client receives names nobody (julien_asked: a former parameter name, kept as an alias).
        if (seen.Replace("julien_asked", "", StringComparison.Ordinal).Contains("Julien", StringComparison.Ordinal))
            throw new InvalidDataException("server text sent to every client names a person");
        return ((string?)init["serverInfo"]?["name"] ?? "?", (string?)init["serverInfo"]?["version"] ?? "?", tools.AsArray().Count);
    }

    static readonly JsonObject Initialize = new()
    {
        ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize",
        ["params"] = new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "nexus-pack-check", ["version"] = "1" } },
    };

    static async Task<(JsonNode Init, JsonNode Tools)> OverHttp(string url, JsonObject server)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        string? token = null;
        if ((string?)server["bearer_token_env_var"] is { } variable) token = UserVariable(variable);
        foreach (var header in server["headers"]?.AsObject() ?? [])
        {
            var value = Regex.Replace((string?)header.Value ?? "", @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m => UserVariable(m.Groups[1].Value) ?? "");
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) token = value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : value;
            else http.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, value);
        }
        if (string.IsNullOrEmpty(token)) throw new InvalidDataException("no access token (NEXUS_MCP_TOKEN)");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        string? lastSession = null;
        async Task<JsonNode> Call(JsonObject message, string? session)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json") };
            if (session is not null) request.Headers.Add("Mcp-Session-Id", session);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids)) lastSession = ids.First();
            var body = await response.Content.ReadAsStringAsync();
            // A JSON body, or server-sent events whose data lines carry the reply.
            var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                ? string.Join("", body.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()).Where(l => l.Contains("\"id\"", StringComparison.Ordinal)).Take(1))
                : body;
            var reply = JsonNode.Parse(json) ?? throw new InvalidDataException("empty reply");
            return reply["result"] ?? throw new InvalidDataException($"error reply: {reply["error"]?.ToJsonString()}");
        }
        var init = await Call(Initialize.DeepClone().AsObject(), null);
        var session = lastSession;
        using (var notice = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json") })
        {
            if (session is not null) notice.Headers.Add("Mcp-Session-Id", session);
            using var _ = await http.SendAsync(notice);
        }
        var tools = await Call(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/list" }, session);
        return (init, tools["tools"] ?? throw new InvalidDataException("no tools"));
    }

    static async Task<(JsonNode Init, JsonNode Tools)> OverStdio(JsonObject server)
    {
        var command = Expand((string?)server["command"] ?? throw new InvalidDataException("neither url nor command"));
        // A bare name (npx, uvx) is found the way the AI app finds it: PATH and PATHEXT; a .cmd goes through cmd.exe.
        if (!Path.IsPathRooted(command) && !command.Contains('\\', StringComparison.Ordinal) && !command.Contains('/', StringComparison.Ordinal))
        {
            var found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(dir => (Path.HasExtension(command) ? [""] : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';'))
                    .Select(ext => Path.Combine(dir, command + ext.ToLowerInvariant())))
                .FirstOrDefault(File.Exists);
            if (found is not null) command = found;
        }
        var viaCmd = command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(viaCmd ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : command)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
        };
        if ((string?)server["cwd"] is { } cwd) start.WorkingDirectory = Expand(cwd);
        // Arguments as the AI app passes them: cmd.exe (Codex's launcher) expands %VAR% itself.
        if (viaCmd) foreach (var arg in new[] { "/d", "/c", command }) start.ArgumentList.Add(arg);
        foreach (var arg in server["args"]?.AsArray() ?? []) start.ArgumentList.Add(command.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) ? (string)arg! : Expand((string)arg!));
        foreach (var (key, value) in server["env"]?.AsObject() ?? []) start.Environment[key] = Expand((string?)value ?? "");
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command} did not start");
        _ = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            async Task<JsonNode> Call(JsonObject message, int id)
            {
                await process.StandardInput.WriteLineAsync(message.ToJsonString());
                await process.StandardInput.FlushAsync();
                while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                {
                    JsonNode? reply;
                    try { reply = JsonNode.Parse(line); } catch (System.Text.Json.JsonException) { continue; }
                    if ((int?)reply?["id"] == id) return reply!["result"] ?? throw new InvalidDataException($"error reply: {reply["error"]?.ToJsonString()}");
                }
                throw new InvalidDataException($"server exited (code {(process.HasExited ? process.ExitCode : -1)}) before replying to request {id}");
            }
            var init = await Call(Initialize.DeepClone().AsObject(), 1);
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            var tools = await Call(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/list" }, 2);
            return (init, tools["tools"] ?? throw new InvalidDataException("no tools"));
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("no reply within 120 s");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
