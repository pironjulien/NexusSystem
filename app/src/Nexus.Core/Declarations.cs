using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>What an AI app's own configuration really says about a product (same states as get-nexus.ps1).</summary>
public enum DeclarationState
{
    Declared,
    Missing,
    /// <summary>Turned off by the user (enabled = false, a plugin set to false): kept as is, never turned on again.</summary>
    Disabled,
    /// <summary>Declared, but its command cannot be found.</summary>
    Invalid,
}

/// <summary>
/// Whether an AI app declares a product's MCP server in its own configuration, and in what state: the same checks as
/// get-nexus.ps1 (Get-ClaudeCodeState, Get-ClaudeDesktopState, Get-CodexState, Get-AntigravityState).
/// </summary>
public static partial class Declarations
{
    public const string Marketplace = "nexus-plugins";

    public static bool IsDeclared(Product product, AiApp app, UserFolders folders) => State(product, app, folders) == DeclarationState.Declared;

    public static DeclarationState State(Product product, AiApp app, UserFolders folders) => product.Server is null ? DeclarationState.Missing : app switch
    {
        AiApp.ClaudeCode => ClaudeCode(product, folders),
        AiApp.Codex => Codex(product, folders),
        _ => Antigravity(product, folders),
    };

    /// <summary>
    /// A device product (NexusKeypad): the state its plugin writes when loaded (~\.nexus\status\keypad.json), else
    /// one of its editions in the folder the Logitech plugin service loads: NexusKeypad (complete edition),
    /// TrinitePad (Marketplace "AI Coding Agents", or a build from the sources), AiAgentsPc, PcDashboard.
    /// </summary>
    public static bool DeviceInstalled(Product product, UserFolders folders)
    {
        if (product.Device is null) return false;
        if (File.Exists(Path.Combine(folders.Nexus, "status", "keypad.json"))) return true;
        var plugins = Path.Combine(folders.LocalAppData, "Logi", "LogiPluginService", "Plugins");
        return KeypadEditions.Any(edition => Directory.Exists(Path.Combine(plugins, edition)));
    }

    static readonly string[] KeypadEditions = ["NexusKeypad", "TrinitePad", "AiAgentsPc", "PcDashboard"];

    static DeclarationState ClaudeCode(Product product, UserFolders folders)
    {
        // A plugin of that name (this catalog's or another marketplace's): true on, false turned off by the user.
        var plugin = product.ClaudePlugin ?? product.Server!;
        var named = new List<JsonProperty>();
        if (ReadJson(folders.ClaudeSettings) is { } root && root.TryGetProperty("enabledPlugins", out var plugins) && plugins.ValueKind == JsonValueKind.Object)
            named.AddRange(plugins.EnumerateObject().Where(p => p.Name.StartsWith(plugin + "@", StringComparison.Ordinal)));
        if (named.Any(p => p.Value.ValueKind == JsonValueKind.True)) return DeclarationState.Declared;
        if (named.Count > 0) return DeclarationState.Disabled;
        // A product declared by its own installer (NexusRelay, or NexusAndroid and NexusBrowser when the plugin route
        // failed): its server in ~\.claude.json.
        if (ReadJson(Path.Combine(folders.Home, ".claude.json")) is { } config &&
            config.TryGetProperty("mcpServers", out var servers) && servers.ValueKind == JsonValueKind.Object &&
            servers.TryGetProperty(product.Server!, out var server))
            return JsonServer(server);
        // Claude Desktop (an extension or claude_desktop_config.json) never proves Claude Code ready: Nexus lives in Code.
        return DeclarationState.Missing;
    }

    static DeclarationState JsonServer(JsonElement server)
    {
        if (server.ValueKind != JsonValueKind.Object) return DeclarationState.Invalid;
        if (server.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True) return DeclarationState.Disabled;
        if (server.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && url.GetString() is { Length: > 0 }) return DeclarationState.Declared;
        return server.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String && CommandExists(command.GetString())
            ? DeclarationState.Declared : DeclarationState.Invalid;
    }

    static DeclarationState Codex(Product product, UserFolders folders)
    {
        if (!File.Exists(folders.CodexConfig)) return DeclarationState.Missing;
        var text = File.ReadAllText(folders.CodexConfig);
        var name = Regex.Escape(product.Server!);
        if (TomlTable(text, $@"^\s*\[mcp_servers\.(""?){name}\1\]\s*(#.*)?$") is { } server)
        {
            if (server.Any(l => Regex.IsMatch(l, @"^\s*enabled\s*=\s*false\b"))) return DeclarationState.Disabled;
            if (server.Any(l => Regex.IsMatch(l, @"^\s*url\s*="))) return DeclarationState.Declared;
            foreach (var line in server)
            {
                var command = Regex.Match(line, @"^\s*command\s*=\s*[""'](.+?)[""']");
                if (command.Success && CommandExists(command.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal))) return DeclarationState.Declared;
            }
            return DeclarationState.Invalid;
        }
        if (TomlTable(text, $@"^\s*\[plugins\.""{name}@[^""]+""\]\s*(#.*)?$") is { } plugin)
            return plugin.Any(l => Regex.IsMatch(l, @"^\s*enabled\s*=\s*false\b")) ? DeclarationState.Disabled : DeclarationState.Declared;
        return DeclarationState.Missing;
    }

    // The lines of one TOML table, up to the next table header (its keys come before any sub-table); null if absent.
    static List<string>? TomlTable(string text, string header)
    {
        List<string>? body = null;
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (Regex.IsMatch(line, @"^\s*\["))
            {
                if (body is not null) break;
                if (Regex.IsMatch(line, header)) body = [];
                continue;
            }
            body?.Add(line);
        }
        return body;
    }

    static DeclarationState Antigravity(Product product, UserFolders folders)
    {
        // Current name first, then a former one (an installation not yet migrated: NexusWindows nexus-computer).
        var name = new[] { product.AntigravityPlugin! }.Concat(product.AntigravityFormerPlugins ?? [])
            .FirstOrDefault(n => File.Exists(Path.Combine(folders.AntigravityPlugins, n, "plugin.json")));
        if (name is null) return DeclarationState.Missing;
        // Turned off in Antigravity: plugins.<name>.enabled = false in ~\.gemini\config\config.json.
        return ReadJson(Path.Combine(folders.Home, ".gemini", "config", "config.json")) is { } config &&
            config.TryGetProperty("plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Object &&
            plugins.TryGetProperty(name, out var entry) && entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False
            ? DeclarationState.Disabled : DeclarationState.Declared;
    }

    /// <summary>A command as an AI app starts it: a full path that exists, or a program found on PATH.</summary>
    public static bool CommandExists(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var expanded = Environment.ExpandEnvironmentVariables(command);
        if (Path.IsPathRooted(expanded)) return File.Exists(expanded);
        if (Path.HasExtension(expanded)) expanded = Path.GetFileNameWithoutExtension(expanded);  // node.exe: PATHEXT adds it back
        return AiApps.OnPath(expanded) is not null;
    }

    static JsonElement? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.Clone();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;  // unreadable: not declared, as the pack reports it
        }
    }
}
