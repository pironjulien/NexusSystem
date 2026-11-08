using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>
/// What the pack does outside its own decisions: AI app command lines, product installers, Git, the maintenance task.
/// Each member is virtual so the tests run the pack's real decisions on a throwaway profile without network,
/// installers or AI processes. Programs run in the console the pack was started from (their colors and progress kept).
/// </summary>
public partial class PackServices(UserFolders folders, IPackOutput output, bool json = false)
{
    public UserFolders Folders { get; } = folders;
    public IPackOutput Output { get; } = output;

    /// <summary>Whether the pack writes in a console that the programs it starts can share.</summary>
    public bool Console { get; init; }

    /// <summary>-Json: one "NEXUS_PACK_EVENT {json}" line per event (found, plan, step, cell, done).</summary>
    public void Event(IReadOnlyDictionary<string, object?> data)
    {
        if (json) Output.Line(PackInstaller.EventLine(data));
    }

    public virtual IReadOnlyList<string> FindAiApps() => AiApps.FindKeys(Folders);

    /// <summary>The folder of the Logitech plugin service (Logi Options+, opened once), where NexusKeypad goes.</summary>
    public virtual bool HasLogiService() => Products.LogiService(Folders);

    public virtual bool HasGit() => OnPath("git.exe") is not null;

    public virtual string? ClaudeCli() => AiApps.FindClaudeCli(Folders);

    public virtual string? CodexCli() => PluginMaintenance.CodexCli(Folders);

    static string? OnPath(string file) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(folder => { try { return Path.Combine(folder, file); } catch (ArgumentException) { return null; } })
        .FirstOrDefault(File.Exists);

    /// <summary>A program in the pack's console (nothing captured), to its end; its exit code.</summary>
    public virtual int RunVisible(string file, IEnumerable<string> arguments, bool cleanModulePath = false)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = !Console };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // A PowerShell 7 parent's module path must not make Windows PowerShell load incompatible modules.
        if (cleanModulePath) start.Environment.Remove("PSModulePath");
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(file)} did not start.");
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>A program whose output the pack reads, bounded in time.</summary>
    public virtual (int Code, string Output, string Error) RunCaptured(string file, IEnumerable<string> arguments) => MaintenanceServices.Run(file, arguments);

    static string WindowsPowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>Git through winget (its official package), then this process's PATH as the registry now has it.</summary>
    public virtual int InstallGit(PackInstaller pack)
    {
        if (OnPath("winget.exe") is not { } winget)
        {
            pack.Message("Git is needed to add the Nexus plugin catalog and winget is missing: install Git from https://git-scm.com/downloads/win, then run this again.", PackColor.Red);
            return 1;
        }
        var code = pack.Step("Git (official package, through winget)", () => RunVisible(winget,
            ["install", "--exact", "--id", "Git.Git", "--source", "winget", "--accept-source-agreements", "--accept-package-agreements"]));
        // A program just installed is on the registry PATH, which this process only gets this way.
        var known = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var scope in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            foreach (var entry in (Environment.GetEnvironmentVariable("Path", scope) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (known.Contains(entry, StringComparer.OrdinalIgnoreCase)) continue;
                known.Add(entry);
            }
        }
        Environment.SetEnvironmentVariable("PATH", string.Join(';', known));
        if (HasGit()) return 0;
        return code != 0 ? code : 1;
    }

    /// <summary>
    /// Claude Code: the plugins of this catalog (server pinned and locked, plus its guide). With the claude command, its
    /// own plugin commands; without it (Claude Desktop's Code tab only), the documented settings.json keys, which Claude
    /// installs from when the next conversations start.
    /// </summary>
    public virtual int InstallClaudePlugins(PackInstaller pack, IReadOnlyList<string> plugins, string catalog)
    {
        const string marketplace = Declarations.Marketplace;
        if (ClaudeCli() is { } cli)
        {
            return pack.Step("Claude Code: Nexus plugins", () =>
            {
                var listed = RunCaptured(cli, ["plugin", "marketplace", "list"]);
                var code = MarketplaceWord().IsMatch(listed.Output + listed.Error)
                    ? RunVisible(cli, ["plugin", "marketplace", "update", marketplace])
                    : RunVisible(cli, ["plugin", "marketplace", "add", catalog]);
                if (code != 0) return code;
                var installed = RunCaptured(cli, ["plugin", "list", "--json"]);
                if (installed.Code != 0) return installed.Code;
                foreach (var plugin in plugins)
                {
                    var id = $"{plugin}@{marketplace}";
                    code = installed.Output.Contains(id, StringComparison.Ordinal)
                        ? RunVisible(cli, ["plugin", "update", id, "--scope", "user", "--json"])
                        : RunVisible(cli, ["plugin", "install", id, "--scope", "user"]);
                    if (code != 0) return code;
                }
                return 0;
            });
        }
        return pack.Step("Claude Code: Nexus plugins (settings.json)", () =>
        {
            var path = Folders.ClaudeSettings;
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            var settings = (text.Trim().Length == 0 ? null : JsonNode.Parse(text, null, new JsonDocumentOptions
                { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject) ?? [];
            var before = Compact(settings);
            if (settings["extraKnownMarketplaces"] is not JsonObject known) settings["extraKnownMarketplaces"] = known = [];
            if (!known.ContainsKey(marketplace))
            {
                known[marketplace] = new JsonObject
                {
                    ["source"] = Path.Exists(catalog)
                        ? new JsonObject { ["source"] = "directory", ["path"] = Path.GetFullPath(catalog) }
                        : new JsonObject { ["source"] = "github", ["repo"] = catalog },
                };
            }
            if (settings["enabledPlugins"] is not JsonObject enabled) settings["enabledPlugins"] = enabled = [];
            foreach (var plugin in plugins) enabled[$"{plugin}@{marketplace}"] = true;
            if (Compact(settings) == before)
            {
                pack.Message($"Already declared in {path}.");
                return 0;
            }
            JsonWithBackup.Write(path, settings, Folders);
            pack.Message($"Declared in {path} (installed by Claude when the next conversations start).");
            return 0;
        });
    }

    static string Compact(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) node.WriteTo(writer);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [GeneratedRegex($@"(?m)\b{Declarations.Marketplace}\b")]
    private static partial Regex MarketplaceWord();

    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)?pironjulien/NexusSystem(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficialCatalog();

    /// <summary>
    /// Codex's Nexus catalog: the CLI owns its configuration. An existing catalog is kept only when it is this same
    /// source (GitHub shorthand, HTTPS and SSH spell the same repository; a local folder is read in place, which is also
    /// how the pack check verifies the exact catalog about to be published); another source is preserved and refused.
    /// A Git catalog is refreshed, and the new hook versions of this catalog's installed plugins approved.
    /// </summary>
    public virtual int InstallCodexMarketplace(PackInstaller pack, string cli, string catalog) => pack.Step("Codex: Nexus plugin catalog", () =>
    {
        var listed = RunCaptured(cli, ["plugin", "marketplace", "list", "--json"]);
        if (listed.Code != 0) throw new InvalidOperationException("Codex could not inspect its existing marketplaces.");
        using var document = JsonDocument.Parse(listed.Output);
        var existing = document.RootElement.Item("marketplaces") is { ValueKind: JsonValueKind.Array } all
            ? all.EnumerateArray().Where(m => m.Text("name") == Declarations.Marketplace).ToList() : [];
        if (existing.Count > 1) throw new InvalidOperationException("Codex reported an ambiguous Nexus marketplace; no source was replaced.");
        var local = Directory.Exists(catalog);
        if (existing.Count == 1)
        {
            var root = existing[0].Text("root");
            var sameLocal = local && !string.IsNullOrEmpty(root) &&
                Path.GetFullPath(catalog).TrimEnd('\\', '/').Equals(Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            var source = existing[0].Item("marketplaceSource");
            var sameGit = !local && OfficialCatalog().IsMatch(catalog) && source?.Text("sourceType") == "git" && OfficialCatalog().IsMatch(source.Value.Text("source") ?? "");
            if (!sameLocal && !sameGit) throw new InvalidOperationException("The existing Nexus marketplace uses another source; it was preserved.");
        }
        else if (RunVisible(cli, ["plugin", "marketplace", "add", catalog, "--json"]) != 0)
            throw new InvalidOperationException("Codex could not add the Nexus marketplace.");
        if (local) return 0;
        var code = RunVisible(cli, ["plugin", "marketplace", "upgrade", Declarations.Marketplace, "--json"]);
        if (code != 0) return code;
        // The upgrade refreshes every installed plugin of this catalog (Relay and Keypad carry hooks): approve this
        // catalog's new hook versions here too.
        ApproveHooks(pack, cli, null, "Codex", "of the Nexus plugins");
        return 0;
    });

    /// <summary>Codex: the plugin (its MCP declaration and guide), checked in Codex's own cache, and its hooks approved.</summary>
    public virtual int InstallCodexPlugin(PackInstaller pack, string cli, string name) => pack.Step($"Codex: {name} tools and guide", () =>
    {
        var id = $"{name}@{Declarations.Marketplace}";
        var run = RunCaptured(cli, ["plugin", "add", id, "--json"]);
        if (run.Output.Trim().Length > 0) pack.Message(run.Output.TrimEnd());
        if (run.Code != 0) return run.Code;
        using var installed = JsonDocument.Parse(run.Output);
        if (installed.RootElement.Text("pluginId") != id || installed.RootElement.Text("installedPath") is not { Length: > 0 } path ||
            !PluginMaintenance.CodexPluginInstalled(name, path, Folders))
            throw new InvalidOperationException("Codex did not return an installed plugin with its MCP declaration and guide.");
        ApproveHooks(pack, cli, id, name, "of this version");
        return 0;
    });

    /// <summary>
    /// Installing the product is the consent Codex asks again for each new hook version: the signed engine lists the
    /// hooks and records their current hashes. When it cannot, Codex asks once.
    /// </summary>
    protected virtual void ApproveHooks(PackInstaller pack, string cli, string? pluginId, string who, string which)
    {
        try
        {
            var approved = PluginMaintenance.ApproveHooks(cli, pluginId, Folders);
            if (approved > 0) pack.Message(pluginId is null ? $"Codex: {approved} hook(s) {which} approved." : $"{who}: {approved} Codex hook(s) {which} approved.");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            pack.Message(pluginId is null ? $"Codex: hooks not approved in advance ({error.Message}); Codex will ask once."
                : $"{who}: Codex hooks not approved in advance ({error.Message}); Codex will ask once.", PackColor.Yellow);
        }
    }

    /// <summary>
    /// A product's official installer: the get.ps1 of its latest release, checked against that release's
    /// SHA256SUMS.txt (and its build provenance when gh is there), run with -File in Windows PowerShell, in this console.
    /// </summary>
    public virtual int RunProductInstaller(PackInstaller pack, PackProduct product, string work)
    {
        PublishedFile release;
        try
        {
            release = new MaintenanceServices(Folders).PublishedInstallerAsync(product.Repository, work, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            pack.Message();
            pack.Message($"== {product.Name}", PackColor.Cyan);
            pack.Message($"{product.Name}: its release installer could not be fetched and checked ({error.Message}).", PackColor.Red);
            return 1;
        }
        return pack.Step($"{product.Name} {release.Tag}", () =>
            RunVisible(WindowsPowerShell, ["-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", release.Path], cleanModulePath: true));
    }

    /// <summary>
    /// The one shared update authority: this engine at its durable path and the "NexusSystem Update" task. With
    /// -MaintenanceFrom (the pack check), the task names the release being checked (its catalogue and engine folder).
    /// </summary>
    public virtual void InstallMaintenance(PackOptions options)
    {
        var test = options.MaintenanceFrom is { } from ? new TestRelease(Path.GetFullPath(options.Catalog), Path.GetFullPath(from)) : null;
        new MaintenanceSetup(Folders).Install(MaintenanceSetup.Self(test), test, start: false);
    }

    /// <summary>NexusBrowser's own guide: Chrome opened on its extensions page.</summary>
    public virtual void ShowBrowserGuide(string guide) =>
        RunVisible(WindowsPowerShell, ["-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", guide, "-WaitSeconds", "0"], cleanModulePath: true);
}
