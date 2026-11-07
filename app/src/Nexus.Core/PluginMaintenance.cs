using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Windows.Management.Deployment;

namespace Nexus.Core;

/// <summary>
/// The public catalogue plugins of Claude Code and Codex, refreshed through each AI app's own signed command line
/// (they own their configuration), plus the approval of the Nexus plugins' new hook versions in Codex.
/// </summary>
public static partial class PluginMaintenance
{
    const string Catalog = "pironjulien/NexusSystem";

    public static void Refresh(MaintenanceProduct product, IReadOnlyList<MaintenanceRoute> routes, UserFolders folders)
    {
        var plugin = Declarations.PluginName(product.Product);
        var id = plugin + "@" + Declarations.Marketplace;
        foreach (var route in routes.Where(r => r.Route == "catalogue"))
        {
            if (route.Key == "codex")
            {
                var cli = CodexCli(folders) ?? throw new InvalidOperationException("The signed Codex CLI is unavailable; no configuration was replaced.");
                if (!CodexMarketplace(cli, folders)) throw new InvalidOperationException("Codex catalogue refresh failed.");
                if (!CodexPlugin(cli, plugin, folders)) throw new InvalidOperationException("Codex plugin refresh failed.");
                var (code, output, _) = MaintenanceServices.Run(cli, ["plugin", "list", "--json"]);
                if (code != 0) throw new InvalidOperationException("Codex could not report its installed plugin.");
                using var listed = JsonDocument.Parse(output);
                var entry = listed.RootElement.Item("installed") is { ValueKind: JsonValueKind.Array } installed
                    ? installed.EnumerateArray().FirstOrDefault(e => e.Text("pluginId") == id || e.Text("id") == id) : default;
                route.PluginVersion = entry.ValueKind == JsonValueKind.Object ? entry.Item("version")?.ToString() : null;
                route.Reason = "Plugin files verified; active conversations may still require reloading.";
            }
            else if (route.Key == "claude-code")
            {
                var cli = AiApps.FindClaudeCli(folders) ?? throw new InvalidOperationException("The trusted Claude Code CLI is unavailable; plugin activation remains pending.");
                if (MaintenanceServices.Run(cli, ["plugin", "marketplace", "update", Declarations.Marketplace]).Code != 0)
                    throw new InvalidOperationException("Claude catalogue refresh failed.");
                if (MaintenanceServices.Run(cli, ["plugin", "update", id, "--scope", "user", "--json"]).Code != 0)
                    throw new InvalidOperationException("Claude plugin update failed.");
                var entry = Maintainer.ClaudePluginEntry(folders, id);
                if (entry?.Text("installPath") is not { Length: > 0 } path || !File.Exists(Path.Combine(path, "skills", plugin, "SKILL.md")))
                    throw new InvalidOperationException("Claude did not return an installed guide; activation remains pending.");
                route.PluginVersion = entry.Value.Item("version")?.ToString();
                var settingsPath = folders.ClaudeSettings;
                if (File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath), null, new JsonDocumentOptions
                    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is JsonObject settings &&
                    settings["extraKnownMarketplaces"]?[Declarations.Marketplace] is JsonObject marketplace && !marketplace.ContainsKey("autoUpdate"))
                {
                    marketplace["autoUpdate"] = true;
                    JsonWithBackup.Write(settingsPath, settings, folders);
                }
                route.Reason = "Plugin updated on disk; /reload-plugins or a new session activates it.";
            }
        }
    }

    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)?pironjulien/NexusSystem(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficialCatalog();

    /// <summary>Codex's Nexus marketplace, added once from the official repository and upgraded; another source is kept.</summary>
    static bool CodexMarketplace(string cli, UserFolders folders)
    {
        var (code, output, _) = MaintenanceServices.Run(cli, ["plugin", "marketplace", "list", "--json"]);
        if (code != 0) return false;
        using var listed = JsonDocument.Parse(output);
        var existing = listed.RootElement.Item("marketplaces") is { ValueKind: JsonValueKind.Array } all
            ? all.EnumerateArray().Where(m => m.Text("name") == Declarations.Marketplace).ToList() : [];
        if (existing.Count > 1) return false;  // ambiguous: no source is replaced
        if (existing.Count == 1)
        {
            // GitHub shorthand, HTTPS and SSH identify this same repository; changing spelling is not a new source.
            var source = existing[0].Item("marketplaceSource");
            if (source?.Text("sourceType") != "git" || !OfficialCatalog().IsMatch(source.Value.Text("source") ?? "")) return false;
        }
        else if (MaintenanceServices.Run(cli, ["plugin", "marketplace", "add", Catalog, "--json"]).Code != 0) return false;
        if (MaintenanceServices.Run(cli, ["plugin", "marketplace", "upgrade", Declarations.Marketplace, "--json"]).Code != 0) return false;
        // The upgrade refreshes every installed plugin of this catalog (Relay and Keypad carry hooks): approve this
        // catalog's new hook versions here too. Codex asks once otherwise.
        TryApproveHooks(cli, null, folders);
        return true;
    }

    static bool CodexPlugin(string cli, string name, UserFolders folders)
    {
        var id = name + "@" + Declarations.Marketplace;
        var (code, output, _) = MaintenanceServices.Run(cli, ["plugin", "add", id, "--json"]);
        if (code != 0) return false;
        using var installed = JsonDocument.Parse(output);
        if (installed.RootElement.Text("pluginId") != id || installed.RootElement.Text("installedPath") is not { Length: > 0 } path ||
            !CodexPluginInstalled(name, path, folders)) return false;
        TryApproveHooks(cli, id, folders);
        return true;
    }

    static void TryApproveHooks(string cli, string? pluginId, UserFolders folders)
    {
        try { ApproveHooks(cli, pluginId, folders); }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            // Not approved in advance: Codex asks once.
        }
    }

    /// <summary>The plugin is enabled in config.toml and its cached files hold the guide and server its manifest names.</summary>
    public static bool CodexPluginInstalled(string name, string installedPath, UserFolders folders)
    {
        var config = folders.CodexConfig;
        if (!File.Exists(config) || CodexToml.PluginState(File.ReadAllText(config), name) != "enabled") return false;
        var cache = Path.Combine(Path.GetDirectoryName(config)!, "plugins", "cache", Declarations.Marketplace, name);
        if (!Directory.Exists(installedPath) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(installedPath).TrimEnd('\\')), Path.GetFullPath(cache), StringComparison.OrdinalIgnoreCase)) return false;
        var manifest = JsonFiles.Read(Path.Combine(installedPath, ".codex-plugin", "plugin.json"));
        if (manifest?.Text("name") != name || manifest.Value.Text("skills") is not { } skills || manifest.Value.Text("mcpServers") is not { } servers ||
            !skills.StartsWith("./", StringComparison.Ordinal) || !servers.StartsWith("./", StringComparison.Ordinal)) return false;
        return File.Exists(Path.Combine(installedPath, skills[2..], name, "SKILL.md")) && File.Exists(Path.Combine(installedPath, servers[2..]));
    }

    /// <summary>
    /// Codex asks again for every hook of a plugin whose files changed, so each Nexus update meant a new approval.
    /// Installing the product is that consent: the signed engine itself lists this plugin's hooks (without a plugin,
    /// those of every installed plugin of this catalog) and records their current hashes through its app server; other
    /// catalogs' plugins and disabled hooks are left alone.
    /// </summary>
    static int ApproveHooks(string cli, string? pluginId, UserFolders folders)
    {
        var start = new ProcessStartInfo(cli, "app-server")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Codex reads JSON lines: its standard input must start without a byte order mark.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        using var server = Process.Start(start) ?? throw new InvalidOperationException("Codex app-server did not start.");
        try
        {
            server.ErrorDataReceived += (_, _) => { };
            server.BeginErrorReadLine();
            server.StandardInput.NewLine = "\n";
            JsonElement Send(string method, JsonObject parameters)
            {
                var id = Guid.NewGuid().ToString("N");
                server.StandardInput.WriteLine(Compact(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }));
                server.StandardInput.Flush();
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (true)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) throw new TimeoutException($"no answer to {method}");
                    var read = server.StandardOutput.ReadLineAsync();
                    if (!read.Wait(remaining)) throw new TimeoutException($"no answer to {method}");
                    var line = read.Result ?? throw new InvalidOperationException($"Codex stopped before answering {method}");
                    using var reply = JsonDocument.Parse(line);
                    if (reply.RootElement.Item("id")?.ToString() != id) continue;
                    if (reply.RootElement.Item("error") is { ValueKind: JsonValueKind.Object } error)
                        throw new InvalidOperationException($"{method} refused: {error.Text("message")}");
                    return reply.RootElement.Item("result")?.Clone() ?? default;
                }
            }
            Send("initialize", new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "nexus-system", ["version"] = "1" } });
            server.StandardInput.WriteLine("{\"method\":\"initialized\"}");
            var listed = Send("hooks/list", new JsonObject { ["cwds"] = new JsonArray(folders.Home) });
            var trust = new JsonObject();
            if (listed.Item("data") is { ValueKind: JsonValueKind.Array } data)
            {
                foreach (var hook in data.EnumerateArray().SelectMany(d => d.Item("hooks") is { ValueKind: JsonValueKind.Array } hooks ? hooks.EnumerateArray() : []))
                {
                    var owner = hook.Text("pluginId") ?? "";
                    var ours = pluginId is not null ? owner == pluginId : owner.EndsWith("@" + Declarations.Marketplace, StringComparison.Ordinal);
                    if (ours && hook.Item("enabled")?.ValueKind == JsonValueKind.True && hook.Text("trustStatus") is "untrusted" or "modified" &&
                        hook.Text("currentHash") is { Length: > 0 } hash && hook.Text("key") is { } key)
                        trust[key] = new JsonObject { ["trusted_hash"] = hash };
                }
            }
            if (trust.Count > 0)
            {
                Send("config/batchWrite", new JsonObject
                {
                    ["edits"] = new JsonArray(new JsonObject { ["keyPath"] = "hooks.state", ["value"] = trust, ["mergeStrategy"] = "upsert" }),
                    ["reloadUserConfig"] = true,
                });
            }
            return trust.Count;
        }
        finally
        {
            if (!server.HasExited) server.Kill();
        }
    }

    static string Compact(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) node.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The signed Codex engine: on PATH (or in its npm package), the running desktop app's engine, the desktop's own
    /// bin folder, or the Store package. Never another download, nor an executable that OpenAI did not sign.
    /// </summary>
    public static string? CodexCli(UserFolders folders)
    {
        var candidates = new List<string>();
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(folder)) continue;
            var engine = Path.Combine(folder, "codex.exe");
            if (File.Exists(engine)) candidates.Add(engine);
            var package = Path.Combine(folder, "node_modules", "@openai", "codex");
            if (Directory.EnumerateFiles(folder, "codex.*").Any() && Directory.Exists(package))
                candidates.AddRange(Directory.EnumerateFiles(package, "codex.exe", SearchOption.AllDirectories));
        }
        try
        {
            foreach (var process in NativeProcesses.Read().Where(p => p.Name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase) && p.ExecutablePath is not null))
            {
                var engine = Path.Combine(Path.GetDirectoryName(process.ExecutablePath!)!, "resources", "codex.exe");
                candidates.Add(File.Exists(engine) ? engine : process.ExecutablePath!);
            }
        }
        catch (System.ComponentModel.Win32Exception) { /* no process list: the other places */ }
        var desktop = Path.Combine(folders.LocalAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(desktop))
            candidates.AddRange(new DirectoryInfo(desktop).EnumerateDirectories().OrderByDescending(d => d.LastWriteTime).Select(d => Path.Combine(d.FullName, "codex.exe")));
        try
        {
            candidates.AddRange(new PackageManager().FindPackagesForUser("").Where(p => p.Id.Name == "OpenAI.Codex")
                .Select(p => Path.Combine(p.InstalledPath, "app", "resources", "codex.exe")));
        }
        catch (Exception error) when (error is UnauthorizedAccessException or COMException or ArgumentException) { /* no Store package list */ }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(c => File.Exists(c) && Authenticode.IsSignedBy(c, "OpenAI OpCo, LLC"));
    }
}

/// <summary>
/// A user's JSON file replaced whole and safely: a backup first (same access rules) under ~\.nexus\pack\backups, then
/// an adjacent flushed file renamed over it in one step (no delete/rename gap), keeping its access rules.
/// </summary>
public static partial class JsonWithBackup
{
    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string source, string destination, uint flags);

    public static void Write(string path, JsonNode value, UserFolders folders)
    {
        FileSecurity? access = null;
        if (File.Exists(path))
        {
            access = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
            var backups = Path.Combine(folders.Nexus, "pack", "backups");
            Directory.CreateDirectory(backups);
            var backup = Path.Combine(backups, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}");
            using (new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            new FileInfo(backup).SetAccessControl(Copy(access));
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var target = new FileStream(backup, FileMode.Open, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
            target.Flush(flushToDisk: true);
        }
        else Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.nexus-{Guid.NewGuid():N}.tmp";
        try
        {
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            if (access is not null) new FileInfo(temporary).SetAccessControl(Copy(access));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) value.WriteTo(writer);
            using (var file = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                file.Write(stream.ToArray());
                file.Flush(flushToDisk: true);
            }
            // MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH, same volume: never a copy/delete fallback.
            if (!MoveFileEx(temporary, path, 0x1 | 0x8)) throw new IOException($"{path} was not replaced.", Marshal.GetLastPInvokeError());
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary); // never committed: this run's own temporary file
        }
    }

    static FileSecurity Copy(FileSecurity access)
    {
        var copy = new FileSecurity();
        copy.SetSecurityDescriptorBinaryForm(access.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
        return copy;
    }
}
