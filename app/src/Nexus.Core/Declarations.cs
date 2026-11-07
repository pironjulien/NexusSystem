using System.Text.Json;

namespace Nexus.Core;

/// <summary>What an AI app's own configuration really says about a product.</summary>
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
/// Whether an AI app declares a product's MCP server in its own configuration, and in what state. The app's pages and
/// the hourly maintenance read these same checks (get-nexus.ps1 keeps its own copy for the one-line install).
/// </summary>
public static class Declarations
{
    public const string Marketplace = "nexus-plugins";

    public static bool IsDeclared(Product product, AiApp app, UserFolders folders) => State(product, app, folders) == DeclarationState.Declared;

    /// <summary>The state for the app's pages: a configuration this code cannot read is shown as invalid.</summary>
    public static DeclarationState State(Product product, AiApp app, UserFolders folders)
    {
        try
        {
            return Check(product, AiApps.Key(app), folders);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or CodexTomlException)
        {
            return error is CodexTomlException ? DeclarationState.Invalid : DeclarationState.Missing;
        }
    }

    /// <summary>
    /// The strict check behind <see cref="State"/>, by get-nexus.ps1 name (claude-code, claude-desktop, codex,
    /// antigravity): an unreadable configuration throws instead of being guessed, so maintenance changes nothing from it.
    /// </summary>
    public static DeclarationState Check(Product product, string ai, UserFolders folders)
    {
        if (product.Server is null) return DeclarationState.Missing;
        return ai switch
        {
            "claude-code" => ClaudeCode(product, folders),
            "claude-desktop" => ClaudeDesktop(product, folders),
            "codex" => Codex(product, folders),
            "antigravity" => Antigravity(product, folders),
            _ => DeviceInstalled(product, folders) ? DeclarationState.Declared : DeclarationState.Missing,
        };
    }

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

    /// <summary>
    /// The plugin's keys are on the keypad: its general profile (as NexusKeypad's get.ps1 reads it) holds an action of
    /// one of its editions. False when Logi Options+ never opened the keypad's profile.
    /// </summary>
    public static bool KeypadKeysPlaced(UserFolders folders)
    {
        try
        {
            var app = Path.Combine(folders.LocalAppData, "Logi", "LogiPluginService", "Applications", "Loupedeck70", "@_defaultwin");
            var info = Path.Combine(app, "ApplicationInfo.json");
            if (!File.Exists(info)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(info));
            if (!document.RootElement.TryGetProperty("defaultProfileName", out var name) || name.GetString() is not { Length: > 0 } profile) return false;
            var file = Path.Combine(app, "Profiles", profile, "ProfileInfo.json");
            if (!File.Exists(file)) return false;
            var text = File.ReadAllText(file);
            return KeypadEditions.Any(edition => text.Contains("$" + edition + "___", StringComparison.Ordinal));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The catalog plugin that serves a product in Claude Code and Codex (its server's name otherwise).</summary>
    public static string PluginName(Product product) => product.ClaudePlugin ?? product.Server!;

    /// <summary>The Antigravity plugin names of a product: the current one, then former ones still installed.</summary>
    public static IReadOnlyList<string> AntigravityNames(Product product) =>
        new[] { product.AntigravityPlugin }.OfType<string>().Concat(product.AntigravityFormerPlugins ?? []).ToList();

    static DeclarationState ClaudeCode(Product product, UserFolders folders)
    {
        var plugins = JsonFiles.Read(folders.ClaudeSettings) is { } settings && settings.TryGetProperty("enabledPlugins", out var enabled) &&
            enabled.ValueKind == JsonValueKind.Object ? enabled.EnumerateObject().ToList() : [];
        // This catalog's plugin: true installed and on, false turned off by the user. None (its route failed): the
        // product's own installer then declared it itself, as below.
        var mine = plugins.Where(p => p.Name == PluginName(product) + "@" + Marketplace).ToList();
        if (mine.Count > 0) return mine[0].Value.ValueKind == JsonValueKind.True ? DeclarationState.Declared : DeclarationState.Disabled;
        // Declared by the product's own installer, user scope (~\.claude.json), or a plugin of the same name the user has.
        if (JsonFiles.Read(Path.Combine(folders.Home, ".claude.json")) is { } config && config.TryGetProperty("mcpServers", out var servers) &&
            servers.ValueKind == JsonValueKind.Object && servers.TryGetProperty(product.Server!, out var server))
            return JsonServer(server);
        var named = plugins.Where(p => p.Name.StartsWith(product.Server + "@", StringComparison.Ordinal)).ToList();
        if (named.Any(p => p.Value.ValueKind == JsonValueKind.True)) return DeclarationState.Declared;
        return named.Count > 0 ? DeclarationState.Disabled : DeclarationState.Missing;
    }

    static DeclarationState ClaudeDesktop(Product product, UserFolders folders)
    {
        var desktop = AiApps.DesktopConfigFolders(folders);
        if (desktop.Count == 0) return DeclarationState.Missing;
        var states = new List<DeclarationState>();
        foreach (var folder in desktop)
        {
            if (JsonFiles.Read(Path.Combine(folder, "claude_desktop_config.json")) is not { } config ||
                !config.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object ||
                !servers.TryGetProperty(product.Server!, out var server)) return DeclarationState.Missing;
            states.Add(JsonServer(server));
        }
        return states.Contains(DeclarationState.Disabled) ? DeclarationState.Disabled
            : states.Contains(DeclarationState.Invalid) ? DeclarationState.Invalid : DeclarationState.Declared;
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
        var plugin = CodexToml.PluginState(text, product.Server!);
        if (plugin == "disabled") return DeclarationState.Disabled;
        var tables = CodexToml.ServerTables(text, product.Server!);
        if (tables.Where(t => t.Sub.Length == 0).SelectMany(t => t.Assignments)
            .Any(a => a.Path is ["enabled"] && CodexToml.BooleanValue(a.Value) == false)) return DeclarationState.Disabled;
        foreach (var assignment in tables.Where(t => t.Sub.Length == 0 && !t.Unsupported).SelectMany(t => t.Assignments))
        {
            if (assignment.Path is not ["url"] and not ["command"]) continue;
            string value;
            try { value = CodexToml.StringValue(assignment.Value); }
            catch (CodexTomlException) { continue; }
            if (assignment.Path[0] == "url" || CommandExists(value)) return DeclarationState.Declared;
        }
        if (plugin == "enabled") return DeclarationState.Declared;
        return plugin == "unknown" || tables.Count > 0 ? DeclarationState.Invalid : DeclarationState.Missing;
    }

    static DeclarationState Antigravity(Product product, UserFolders folders)
    {
        var names = AntigravityNames(product);
        // Turned off in Antigravity: plugins.<name>.enabled = false (or false itself) in ~\.gemini\config\config.json.
        // A migration keeps a disabled former identity, including when its old folder has already been removed.
        if (JsonFiles.Read(Path.Combine(folders.Home, ".gemini", "config", "config.json")) is { } config &&
            config.TryGetProperty("plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (!plugins.TryGetProperty(name, out var entry)) continue;
                if (entry.ValueKind == JsonValueKind.False ||
                    entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.False)
                    return DeclarationState.Disabled;
            }
        }
        return names.Any(name => File.Exists(Path.Combine(folders.AntigravityPlugins, name, "plugin.json")))
            ? DeclarationState.Declared : DeclarationState.Missing;
    }

    /// <summary>A command as an AI app starts it: a full path that exists, or a program found on PATH.</summary>
    public static bool CommandExists(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var expanded = Environment.ExpandEnvironmentVariables(command);
        if (Path.IsPathRooted(expanded)) return File.Exists(expanded) || Directory.Exists(expanded);
        if (Path.HasExtension(expanded)) expanded = Path.GetFileNameWithoutExtension(expanded);  // node.exe: PATHEXT adds it back
        return AiApps.OnPath(expanded) is not null;
    }
}

/// <summary>JSON files of the AI apps and products, read as their owners wrote them.</summary>
public static class JsonFiles
{
    static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The root of a JSON file, or null when it is absent or empty; a malformed file throws.</summary>
    public static JsonElement? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        if (text.Trim().Length == 0) return null;
        using var document = JsonDocument.Parse(text, Options);
        return document.RootElement.Clone();
    }

    /// <summary>A string property of an object, or null.</summary>
    public static string? Text(this JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    /// <summary>A property of an object, or null.</summary>
    public static JsonElement? Item(this JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : null;
}
