using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>
/// The hourly maintenance (NexusMaintenance.exe, started by the "NexusSystem Update" task): published releases only,
/// installed products and existing active routes only, running sessions preserved. Exit codes: 0 checked or current,
/// 2 pending (including another run), 1 failed. A loaded version means a live process, never a cache.
/// </summary>
public sealed partial class Maintainer(MaintenanceServices services)
{
    UserFolders Folders => services.Folders;

    public async Task<int> RunAsync(bool current, bool force, CancellationToken cancel = default)
    {
        var root = MaintenanceUpdates.Root(Folders);
        Directory.CreateDirectory(root);
        // The file lock is released by Windows even after an interrupted run. No stale lock heuristics or process kills.
        FileStream gate;
        try { gate = new FileStream(Path.Combine(root, "maintenance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 2; }
        var work = Path.Combine(root, $"run-{Environment.ProcessId}");
        var state = new MaintenanceReceipt();
        string? bootstrap = null;
        int? result = null;
        var preserve = false;
        try
        {
            try
            {
                if (AutoUpdateDisabled())
                {
                    state.State = "disabled";
                    state.Message = "Automatic updates are disabled in nexussystem.json.";
                    result = 0;
                    return 0;
                }
                var previous = JsonFiles.Read(MaintenanceUpdates.StatusPath(Folders));
                if (!force && !current && previous?.Text("nextCheckAt") is { } nextText &&
                    DateTimeOffset.TryParse(nextText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var next) && next > DateTimeOffset.UtcNow)
                {
                    preserve = true;
                    result = previous.Value.Text("state") switch { "failed" => 1, "pending" => 2, _ => 0 };
                    return result.Value;
                }
                Directory.CreateDirectory(work);
                services.WriteStatus(state);
                string tag, version;
                if (services.TestRelease is { } test)
                {
                    // Tests only: the release being checked, from its local catalogue and engine folder, verified by
                    // SHA-256 against this engine; never replaced by, nor compared with, the published release.
                    tag = services.TestReleaseTag(test);
                    version = tag[1..];
                    state.Latest = version;
                    state.Catalogue = tag;
                }
                else if (current)
                {
                    // Explicit qualification of this build: published products only, no self-replacement and no
                    // downgrade or durable copy of a development engine.
                    tag = await services.LatestTagAsync("NexusSystem", cancel).ConfigureAwait(false);
                    if (!ReleaseRules.StableTag().IsMatch(tag)) throw new InvalidDataException("Invalid published catalogue version.");
                    version = tag[1..];
                    state.Latest = version;
                    state.Catalogue = tag;
                }
                else
                {
                    var published = await services.PublishedEngineAsync(cancel).ConfigureAwait(false);
                    (tag, version) = (published.Tag, published.Version);
                    state.Latest = version;
                    state.Catalogue = tag;
                    if (ReleaseRules.Number(version) < ReleaseRules.Number(services.ManagerVersion))
                    {
                        state.State = "pending";
                        state.Message = "This manager is newer than the published release; no downgrade or development payload was propagated.";
                        result = 2;
                        return 2;
                    }
                    if (!published.Sha256.Equals(services.ManagerSha256(), StringComparison.OrdinalIgnoreCase))
                    {
                        // The published engine takes over after this run releases the lock.
                        bootstrap = await services.InstallPublishedEngineAsync(published, work, cancel).ConfigureAwait(false);
                    }
                }
                if (bootstrap is null) await MaintainProductsAsync(state, previous, work, cancel).ConfigureAwait(false);
                if (services.TestRelease is { } checkedRelease)
                    state.Message = $"Test release {state.Catalogue}: catalogue {checkedRelease.Catalog}, engine verified against {checkedRelease.Engines}. {state.Message}";
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                state.State = "failed";
                state.Retryable = true;
                state.Message = error.Message;
            }
        }
        finally
        {
            if (!preserve)
            {
                state.CheckedAt = DateTimeOffset.UtcNow;
                if (state.State == "failed") state.NextCheckAt = state.CheckedAt.AddMinutes(15);
                services.WriteStatus(state);
            }
            // Only this run's own folder is disposable. Never clean product payloads or runtime folders.
            if (Directory.Exists(work) && !File.GetAttributes(work).HasFlag(FileAttributes.ReparsePoint) &&
                Path.GetFullPath(work).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(work, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* next run's folder differs */ }
            }
            await gate.DisposeAsync().ConfigureAwait(false);
        }
        if (bootstrap is not null) return await services.RunEngineAsync(bootstrap, cancel).ConfigureAwait(false);
        return result ?? state.State switch { "failed" => 1, "pending" => 2, _ => 0 };
    }

    bool AutoUpdateDisabled()
    {
        if (JsonFiles.Read(LocalSettings.FilePath(Folders)) is not { ValueKind: JsonValueKind.Object } choices) return false;
        return choices.EnumerateObject().Any(p => p.Name.Equals("autoUpdate", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.False);
    }

    async Task MaintainProductsAsync(MaintenanceReceipt state, JsonElement? previous, string work, CancellationToken cancel)
    {
        var skip = LocalSettings.Read(Folders).Skip;
        var found = services.FindAiApps();
        // The installers' selection, as the PowerShell coordinator left it in its environment from one product to the next.
        var targets = Environment.GetEnvironmentVariable("NEXUS_PACK_TARGETS");
        foreach (var product in MaintenanceProduct.All.Where(p => !skip.Contains(p.Key)))
        {
            if (product.InstallerOnly)
            {
                // NexusKeypad, NexusAndroid, NexusRelay, NexusAGYmail, NexusInDesign, NexusFree: when a newer release
                // exists, its published installer, unattended (NEXUS_PACK), replaces the engine and refreshes the AI
                // connections it owns.
                var installedOnly = services.InstalledVersion(product);
                if (installedOnly is null) continue;
                var simple = new MaintenanceRow { Key = product.Key, InstalledVersion = installedOnly };
                try
                {
                    var tag = await services.LatestTagAsync(product.Repository, cancel).ConfigureAwait(false);
                    if (!ReleaseRules.StableTag().IsMatch(tag)) throw new InvalidDataException("Invalid published version.");
                    var published = ReleaseRules.TagVersion(tag);
                    simple.PublishedVersion = published;
                    if (ReleaseRules.Number(published) > ReleaseRules.Number(installedOnly))
                    {
                        var release = await services.PublishedInstallerAsync(product.Repository, work, cancel).ConfigureAwait(false);
                        if (release.Tag != tag) throw new InvalidOperationException("Release changed while preparing the update; retry on the next cycle.");
                        installedOnly = await RunInstallerAsync(product, simple, release.Path, [], published, targets, cancel).ConfigureAwait(false);
                    }
                    simple.State = "success";
                    simple.Message = "Published version installed.";
                    if (product.Key is not "keypad") services.WriteProductStatus(product.Key, installedOnly, simple.PublishedVersion);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    simple.State = "failed";
                    simple.Message = error.Message;
                }
                state.Products.Add(simple);
                services.WriteStatus(state);
                continue;
            }

            var profiles = found.Where(ai => product.Targets.Contains(ai) && !skip.Contains(ai)).Select(ai => Profile(product, ai)).ToList();
            var active = profiles.Where(p => p.Route is not null and not "custom").ToList();
            var installed = services.InstalledVersion(product);
            if (installed is null && active.Count == 0) continue;
            var row = new MaintenanceRow { Key = product.Key, InstalledVersion = installed, Profiles = profiles };
            try
            {
                if (active.Count == 0)
                {
                    row.State = "kept";
                    row.Message = "No active owned route; disabled and custom routes were preserved.";
                }
                else
                {
                    var tag = await services.LatestTagAsync(product.Repository, cancel).ConfigureAwait(false);
                    if (!ReleaseRules.StableTag().IsMatch(tag)) throw new InvalidDataException("Invalid published version.");
                    var published = ReleaseRules.TagVersion(tag);
                    row.PublishedVersion = published;
                    var source = services.InstallationSource(product);
                    // UpdateOnly is a qualified release contract, never sent to older installers.
                    var minimum = product.Key switch { "windows" => "3.9.5", "browser" => "2.33.5", _ => null };
                    if (minimum is null)
                    {
                        row.State = "unsupported";
                        row.Message = "This product owns its update protocol; automatic installer migration is not qualified.";
                    }
                    else if (installed is null) row.Message = "The existing installation could not be verified; no fresh installation was attempted.";
                    else if (source == "release" && ReleaseRules.Number(published) < ReleaseRules.Number(minimum))
                        row.Message = "The published installer does not yet support preserving existing routes during maintenance.";
                    else
                    {
                        targets = string.Join(',', active.Select(p => p.Key));
                        var retry = product.Key == "browser" && previous?.Item("products") is { ValueKind: JsonValueKind.Array } rows &&
                            rows.EnumerateArray().FirstOrDefault(r => r.Text("key") == product.Key) is { ValueKind: JsonValueKind.Object } old &&
                            old.Item("retryable")?.ValueKind == JsonValueKind.True;
                        var profileRefresh = active.Any(p => p.Key == "antigravity" && p.PluginVersion != published);
                        // A deferred Browser switch is retried on the next bounded cycle even after files were
                        // installed. A plain open AI connection is not a reason to reinstall Windows.
                        if (source == "release" && (ReleaseRules.Number(published) > ReleaseRules.Number(installed) || profileRefresh || retry))
                        {
                            var release = await services.PublishedInstallerAsync(product.Repository, work, cancel).ConfigureAwait(false);
                            if (release.Tag != tag) throw new InvalidOperationException("Release changed while preparing the update; retry on the next cycle.");
                            List<string> parameters = ["UpdateOnly"], arguments = ["-UpdateOnly"];
                            if (product.Key == "windows" && active.Any(p => p.Key == "antigravity"))
                            {
                                parameters.Add("RefreshExistingProfiles");
                                arguments.Add("-RefreshExistingProfiles");
                            }
                            services.CheckInstallerContract(release.Path, parameters);
                            CheckOfficialEnvironment(product.Key);
                            installed = await RunInstallerAsync(product, row, release.Path, arguments, published, targets, cancel).ConfigureAwait(false);
                        }
                        await services.MaintainPluginsAsync(product, active, cancel).ConfigureAwait(false);
                        // A catalogue from another source (a local folder, another repository) is the user's: kept,
                        // reported, neither refreshed nor attested.
                        var keptCatalogues = active.Where(p => p.State == "kept").ToList();
                        foreach (var route in active.Where(p => p.Route == "catalogue" && p.State != "kept"))
                        {
                            if (route.PluginVersion is null || !Version.TryParse(route.PluginVersion, out var plugin) || plugin < ReleaseRules.Number(published))
                            {
                                route.State = "pending";
                                route.ActivationPending = true;
                                route.Reason = "The published catalogue has not supplied this product version yet; distribution remains pending.";
                            }
                        }
                        foreach (var route in active.Where(p => p.Key == "antigravity"))
                        {
                            route.PluginVersion = Profile(product, "antigravity").PluginVersion;
                            if (source == "release" && route.PluginVersion != published)
                                throw new InvalidOperationException("The existing Antigravity plugin has not reached the published version.");
                            if (source != "release")
                                route.Reason = "The existing product-owned Antigravity plugin is preserved; this host has no Nexus public catalogue route.";
                        }
                        var loaded = services.LoadedVersions(product);
                        row.LoadedVersions = [.. loaded.Versions];
                        foreach (var route in active)
                        {
                            if (product.Key == "browser")
                            {
                                if (!route.ActivationPending)
                                {
                                    route.State = "unknown";
                                    route.Reason = "The shared daemon, bridge and extension are observed separately; this does not attest an individual chat guide.";
                                }
                                continue;
                            }
                            if (route.ActivationPending || keptCatalogues.Contains(route)) continue;
                            var agent = route.Key switch { "claude-code" => "claude", "codex" => "codex", "antigravity" => "antigravity", _ => null };
                            var connections = loaded.Agents.Where(a => a.Agent == agent).ToList();
                            if (connections.Count > 0 && connections.All(c => string.Equals(c.Version, installed, StringComparison.OrdinalIgnoreCase)))
                            {
                                route.State = "runtime-verified";
                                route.Reason = "The host owns a matching running engine; guide loading in existing chats remains unknown.";
                            }
                            else
                            {
                                route.ActivationPending = true;
                                route.State = "pending";
                                route.Reason = "No matching host-owned engine attestation; the existing session is preserved.";
                            }
                        }
                        var browserRetry = source == "release" && product.Key == "browser";
                        if (loaded.Unknown || loaded.Versions.Any(v => ReleaseRules.Number(v) != ReleaseRules.Number(installed!)))
                        {
                            (row.State, row.ActivationPending, row.Retryable) = ("pending", true, browserRetry);
                            row.Message = "Installed; an older or unattested running connection is preserved until its safe reload.";
                        }
                        else if (loaded.Versions.Count == 0)
                        {
                            (row.State, row.ActivationPending, row.Retryable) = ("pending", true, browserRetry);
                            row.Message = "Installed; no running connection has attested activation yet.";
                        }
                        else
                        {
                            row.State = "success";
                            row.Message = "Published version installed; observed running engine versions match. Guide loading in existing chats is not attested.";
                        }
                        if (active.Any(p => p.ActivationPending))
                        {
                            row.ActivationPending = true;
                            if (row.State == "success") row.State = "pending";
                        }
                        foreach (var route in keptCatalogues) row.Message += $" {route.Key}: {route.Reason}";
                        if (source == "repository")
                        {
                            if (row.State == "success") row.State = "kept";
                            row.Message = "Repository engine preserved; only public catalogue plugins are maintained. " +
                                row.Message.Replace("Published version installed; ", "", StringComparison.Ordinal);
                        }
                        else if (source == "unknown")
                        {
                            (row.State, row.Retryable) = ("pending", false);
                            row.Message = "Installation provenance is unknown; no product installer was run. " +
                                row.Message.Replace("Published version installed; ", "", StringComparison.Ordinal);
                        }
                    }
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                row.State = "failed";
                row.Message = error.Message;
            }
            state.Products.Add(row);
            services.WriteStatus(state);
        }
        state.ActivationPending = state.Products.Any(r => r.ActivationPending);
        state.Retryable = state.Products.Any(r => r.Retryable);
        state.State = state.Products.Any(r => r.State == "failed") ? "failed" : state.Products.Any(r => r.State == "pending") ? "pending" : "success";
        state.Message = "Installed files and observed runtimes are reported separately; no AI process was stopped.";
    }

    /// <summary>Runs a product's published installer, then checks the installed version. A failure keeps the
    /// installer's output and names it in the status row; a success removes it.</summary>
    async Task<string> RunInstallerAsync(MaintenanceProduct product, MaintenanceRow row, string file, IReadOnlyList<string> arguments,
        string published, string? targets, CancellationToken cancel)
    {
        var log = services.InstallerLogPath(product.Key);
        var code = await services.RunInstallerAsync($"{product.Name} maintenance", file, arguments, log, targets, cancel).ConfigureAwait(false);
        if (code != 0)
        {
            row.Log = log;
            throw new InvalidOperationException($"The product update failed (installer exit code {code}). Installer output: {log}");
        }
        var installed = services.InstalledVersion(product);
        row.InstalledVersion = installed;
        if (installed is null || ReleaseRules.Number(installed) < ReleaseRules.Number(published))
        {
            row.Log = log;
            throw new InvalidOperationException($"The installed version did not reach the published version. Installer output: {log}");
        }
        File.Delete(log);
        return installed;
    }

    /// <summary>A development or test override of the product's own installer is never run unattended.</summary>
    static void CheckOfficialEnvironment(string product)
    {
        var prefix = product switch { "windows" => "NEXUS_WINDOWS_", "browser" => "NEXUS_BROWSER_", _ => null };
        if (prefix is null) return;
        foreach (var suffix in new[] { "SOURCE", "TAG", "ROOT", "PROFILE" })
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(prefix + suffix)))
                throw new InvalidOperationException($"A {product} development/test override is set; unattended maintenance will not execute it.");
        }
    }

    /// <summary>One AI app's route to a product: its declaration state, then whether the route is ours to maintain.</summary>
    public MaintenanceRoute Profile(MaintenanceProduct product, string ai)
    {
        var declared = Declarations.Check(product.Product, ai, Folders);
        if (declared != DeclarationState.Declared)
        {
            return new MaintenanceRoute
            {
                Key = ai,
                State = declared switch { DeclarationState.Missing => "missing", DeclarationState.Disabled => "disabled", _ => "invalid" },
                Reason = "Existing user selection is preserved.",
            };
        }
        var server = product.Product.Server!;
        var plugin = Declarations.PluginName(product.Product) + "@" + Declarations.Marketplace;
        var token = services.Token();
        string route = "custom";
        string? version = null;
        switch (ai)
        {
            case "codex":
            {
                var text = File.ReadAllText(Folders.CodexConfig);
                if (CodexToml.ServerTables(text, server).Count > 0)
                {
                    if (CodexToml.IsOwnedServer(text, server, Folders)) route = "direct";
                }
                else if (CodexToml.PluginState(text, server, Declarations.Marketplace) == "enabled") route = "catalogue";
                break;
            }
            case "claude-code":
            {
                var settings = JsonFiles.Read(Folders.ClaudeSettings);
                var direct = JsonFiles.Read(Path.Combine(Folders.Home, ".claude.json"));
                if (direct?.Item("mcpServers")?.Item(server) is { } declaredServer)
                {
                    if (OwnedJsonRoute(product.Key, declaredServer, "claude", Folders, token)) route = "direct";
                }
                else if (settings?.Item("enabledPlugins")?.Item(plugin)?.ValueKind == JsonValueKind.True) route = "catalogue";
                version = ClaudePluginEntry(Folders, plugin)?.Text("version");
                break;
            }
            case "antigravity":
                foreach (var name in Declarations.AntigravityNames(product.Product))
                {
                    var folder = Path.Combine(Folders.AntigravityPlugins, name);
                    var manifest = JsonFiles.Read(Path.Combine(folder, "plugin.json"));
                    var config = JsonFiles.Read(Path.Combine(folder, "mcp_config.json"));
                    if (manifest is not null && config?.Item("mcpServers")?.Item(server) is { } owned &&
                        OwnedJsonRoute(product.Key, owned, "antigravity", Folders, token))
                    {
                        route = "product";
                        version = manifest.Value.Item("version")?.ToString();
                        break;
                    }
                }
                break;
            case "claude-desktop":
            {
                var owned = true;
                foreach (var folder in AiApps.DesktopConfigFolders(Folders))
                {
                    var config = JsonFiles.Read(Path.Combine(folder, "claude_desktop_config.json"));
                    if (config is null || !OwnedJsonRoute(product.Key, config.Value.Item("mcpServers")?.Item(server), "claude", Folders, token)) owned = false;
                }
                if (owned) route = "direct";
                break;
            }
        }
        var custom = route == "custom";
        return new MaintenanceRoute
        {
            Key = ai,
            State = custom ? "kept" : "unknown",
            Route = route,
            PluginVersion = version,
            Observed = true,
            Reason = custom ? "Custom declaration is preserved." : "Guide loading in an existing conversation is unknown; runtime activation is checked separately.",
        };
    }

    /// <summary>The user-scope entry of a Claude Code plugin in installed_plugins.json, or null.</summary>
    public static JsonElement? ClaudePluginEntry(UserFolders folders, string plugin)
    {
        var entries = JsonFiles.Read(Path.Combine(folders.Home, ".claude", "plugins", "installed_plugins.json"))?.Item("plugins")?.Item(plugin);
        if (entries is not { } list) return null;
        var items = list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [list];
        return items.FirstOrDefault(e => e.Text("scope") == "user") is { ValueKind: JsonValueKind.Object } entry ? entry : null;
    }

    [GeneratedRegex("^(versions/[0-9a-f]{16}|app/engine|native)/NexusBrowser\\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex BrowserEngine();

    static readonly string[] JsonRouteKeys = ["type", "command", "args", "url", "headers", "disabled", "enabled", "startup_timeout_sec", "bearer_token_env_var"];

    /// <summary>
    /// A JSON MCP declaration (Claude Code, Claude Desktop, an Antigravity plugin) exactly as the product's installer
    /// writes it: its engine and arguments, or its local daemon. Anything else belongs to the user.
    /// </summary>
    public static bool OwnedJsonRoute(string product, JsonElement? declared, string agent, UserFolders folders, string? token)
    {
        if (declared is not { ValueKind: JsonValueKind.Object } server) return false;
        if (server.Item("disabled")?.ValueKind == JsonValueKind.True || server.Item("enabled")?.ValueKind == JsonValueKind.False) return false;
        var names = server.EnumerateObject().Select(p => p.Name).ToList();
        string Arguments() => server.Item("args") is { ValueKind: JsonValueKind.Array } args ? string.Join('|', args.EnumerateArray().Select(a => a.ToString())) : server.Item("args")?.ToString() ?? "";
        bool StdioType() => server.Text("type") is not { Length: > 0 } type || type.Equals("stdio", StringComparison.OrdinalIgnoreCase);
        if (product == "browser")
        {
            if (server.Text("command") is { Length: > 0 } command)
            {
                if (names.Any(n => n is not ("type" or "command" or "args" or "env")) || !StdioType()) return false;
                if (!Path.IsPathRooted(command)) return false;
                var engines = Path.GetFullPath(Path.Combine(folders.LocalAppData, "NexusBrowser")).TrimEnd('\\') + "\\";
                var full = Path.GetFullPath(command);
                if (!full.StartsWith(engines, StringComparison.OrdinalIgnoreCase)) return false;
                var env = server.Item("env");
                return BrowserEngine().IsMatch(full[engines.Length..].Replace('\\', '/')) && Arguments() == "--mcp" &&
                    env is { ValueKind: JsonValueKind.Object } variables && variables.EnumerateObject().Count() == 1 &&
                    string.Equals(variables.Text("NEXUS_BROWSER_AGENT"), agent, StringComparison.OrdinalIgnoreCase);
            }
            var urlKeys = names.Where(n => n is "url" or "serverUrl" or "httpUrl").ToList();
            var headers = server.Item("headers");
            var authorization = headers?.Text("Authorization") ?? "";
            var ownedHeader = authorization == "Bearer ${NEXUS_MCP_TOKEN}" || token is { Length: > 0 } && authorization == "Bearer " + token;
            return names.Count == 2 && urlKeys.Count == 1 && server.Text(urlKeys[0]) == $"http://127.0.0.1:16182/mcp?agent={agent}" &&
                headers is { ValueKind: JsonValueKind.Object } list && list.EnumerateObject().Count() == 1 && ownedHeader;
        }
        if (names.Any(n => !JsonRouteKeys.Contains(n))) return false;
        if (product == "windows")
        {
            if (names.Any(n => n is not ("type" or "command" or "args" or "env" or "disabled" or "enabled" or "startup_timeout_sec")) || !StdioType() ||
                server.Item("env") is { ValueKind: JsonValueKind.Object } env && env.EnumerateObject().Any()) return false;
            var stable = Path.Combine(folders.Home, ".nexus", "nexus-windows", "NexusWindows.exe");
            var command = Environment.ExpandEnvironmentVariables(server.Text("command") ?? "").Replace("${USERPROFILE}", folders.Home, StringComparison.Ordinal);
            return command.Equals(stable, StringComparison.OrdinalIgnoreCase) && Arguments() == $"--mcp|--agent|{agent}";
        }
        var port = product switch { "android" => 16180, "relay" => 16181, _ => 0 };
        if (port == 0) return false;
        return Uri.TryCreate(server.Text("url"), UriKind.Absolute, out var uri) && uri.Scheme == "http" && uri.Host == "127.0.0.1" &&
            uri.Port == port && uri.AbsolutePath == "/mcp" && (uri.Query.Length == 0 || uri.Query == $"?agent={agent}");
    }
}
