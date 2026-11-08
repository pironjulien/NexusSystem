using System.Text;
using System.Text.Json;

namespace Nexus.Core;

/// <summary>A console color of the pack's messages (the same as get-nexus.ps1 used before 1.8.0).</summary>
public enum PackColor
{
    Gray,
    DarkGray,
    Cyan,
    Green,
    Yellow,
    Red,
}

/// <summary>Where the pack writes: the console it was started from, or a test's buffer.</summary>
public interface IPackOutput
{
    /// <summary>Text without a line break.</summary>
    void Write(string text, PackColor color = PackColor.Gray);

    /// <summary>A whole line.</summary>
    void Line(string text = "", PackColor color = PackColor.Gray) => Write(text + Environment.NewLine, color);
}

/// <summary>NexusMaintenance.exe --install: the options of get-nexus.ps1's installation mode.</summary>
public sealed record PackOptions(IReadOnlyList<string> Only, IReadOnlyList<string> Skip, bool DryRun = false, bool NoGuide = false,
    string Catalog = PackOptions.OfficialCatalog, bool Json = false, string? MaintenanceFrom = null)
{
    public const string OfficialCatalog = "pironjulien/NexusSystem";
}

/// <summary>A product of the pack: its release repository, the AI apps it serves and its routes.</summary>
public sealed record PackProduct(Product Product, string Repository, IReadOnlyList<string> Targets, bool Engine, string? Plugin,
    IReadOnlyList<string> Antigravity, bool OptIn)
{
    public string Key => Product.Key;
    public string Name => Product.Name;
    public string? Server => Product.Server;
    public string? Device => Product.Device;
    public int MinAis => Product.MinAis;

    /// <summary>
    /// The products, in the pack's order. Each AI app is served by exactly one route: the catalog plugin (Claude Code,
    /// Codex) or the product's installer. Targets: the AI apps a product serves (none: a device, the MX Creative Keypad).
    /// NexusFree is opt-in: it gives the account SYSTEM rights, so only an explicit selection installs it.
    /// </summary>
    public static readonly IReadOnlyList<PackProduct> All = MaintenanceProduct.All.Select(m => new PackProduct(m.Product, m.Repository, m.Targets,
        Engine: m.Product.Device is null, Plugin: m.Key is "agymail" or "keypad" ? null : Declarations.PluginName(m.Product),
        Antigravity: Declarations.AntigravityNames(m.Product), OptIn: m.Key == "free")).ToList();

    public static PackProduct ByKey(string key) => All.First(p => p.Key == key);
}

/// <summary>
/// The Nexus pack (get-nexus.ps1's installation, run by NexusMaintenance.exe --install): it finds Claude Code, Claude
/// Desktop, Codex and Antigravity, shows the plan, gives Claude Code and Codex the plugins of this catalog, then runs
/// each product's own official installer (its release's get.ps1, checked), and reports what each AI app now declares.
/// Run it again at any time: every installer only does what is missing. Exit codes: 0 done, 1 failed, 2 unknown name.
/// </summary>
public sealed class PackInstaller(PackServices services)
{
    const string Ok = "✓";
    const string No = "✗";

    static readonly Dictionary<string, string> AiNames = new()
    {
        ["claude-code"] = "Claude Code",
        ["claude-desktop"] = "Claude Desktop",
        ["codex"] = "Codex",
        ["antigravity"] = "Antigravity",
    };

    /// <summary>The products whose installer configures every AI app found in one run, told the selection in NEXUS_PACK_TARGETS.</summary>
    static readonly string[] OneRunForAll = ["windows", "android", "browser", "relay", "agymail", "indesign", "free"];

    /// <summary>The AI apps that add this catalog with Git.</summary>
    static readonly string[] CatalogClients = ["claude-code", "codex"];

    static readonly (string Key, string Text)[] Examples =
    [
        ("windows", "Open Notepad and write a shopping list: milk, eggs, bread."),
        ("android", "Look at my phone and tell me what is on the screen."),
        ("browser", "Open https://example.com in a background tab and give me the page as Markdown."),
        ("relay", "Use NexusRelay to ask Codex for a second opinion on this file."),
        ("agymail", "Find my unread emails from today and sum them up."),
        ("indesign", "Create an A4 InDesign document with a title on its first page and show me the preview."),
    ];

    UserFolders Folders => services.Folders;

    void Say(string text = "", PackColor color = PackColor.Gray) => services.Output.Line(text, color);

    static string Name(string ai) => AiNames[ai];

    static List<string> Split(IEnumerable<string> names) =>
        names.SelectMany(n => n.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Select(n => n.ToLowerInvariant()).ToList();

    /// <summary>Whether a name is wanted: in -Only when -Only names this kind, never an opt-in product unless named, not in -Skip.</summary>
    static bool Wanted(string key, IReadOnlyCollection<string> universe, List<string> only, List<string> skip)
    {
        var scoped = only.Where(universe.Contains).ToList();
        if (scoped.Count > 0 && !scoped.Contains(key)) return false;
        if (scoped.Count == 0 && PackProduct.All.Any(p => p.Key == key && p.OptIn)) return false;
        return !skip.Contains(key);
    }

    /// <summary>Why a product does not apply on this PC, or null when it does.</summary>
    string? Unfit(PackProduct product, List<string> ais)
    {
        if (product.Device is not null)
            return services.HasLogiService() ? null : $"needs {product.Device}, opened once (https://www.logitech.com/software/logi-options-plus.html)";
        var serving = product.Targets.Where(ais.Contains).ToList();
        if (product.MinAis > 0 && serving.Count < product.MinAis) return $"needs two of {string.Join(", ", product.Targets.Select(Name))}";
        if (serving.Count == 0) return $"for {string.Join(", ", product.Targets.Select(Name))} only";
        return null;
    }

    /// <summary>The cells of a product: its AI apps found here, or "pc" for a device.</summary>
    static List<string> Cells(PackProduct product, List<string> ais) => product.Device is not null ? ["pc"] : ais.Where(product.Targets.Contains).ToList();

    /// <summary>What an AI app really declares for a product: ok, missing, disabled (turned off by the user) or invalid.</summary>
    string Check(PackProduct product, string ai)
    {
        if (ai == "pc") return Declarations.DeviceInstalled(product.Product, Folders) ? "ok" : "missing";
        return Declarations.Check(product.Product, ai, Folders) switch
        {
            DeclarationState.Declared => "ok",
            DeclarationState.Disabled => "disabled",
            DeclarationState.Invalid => "invalid",
            _ => "missing",
        };
    }

    string CodexConfig() => File.Exists(Folders.CodexConfig) ? File.ReadAllText(Folders.CodexConfig) : "";

    /// <summary>The user's own skip list (~\.nexus\nexussystem.json); an unreadable file is reported and ignored.</summary>
    List<string> LocalSkip()
    {
        var file = LocalSettings.FilePath(Folders);
        if (!File.Exists(file)) return [];
        try
        {
            if (JsonFiles.Read(file) is not { ValueKind: JsonValueKind.Object } settings || settings.Item("skip") is not { } skip) return [];
            var items = skip.ValueKind == JsonValueKind.Array ? skip.EnumerateArray().ToList() : [skip];
            return items.Where(i => i.ValueKind == JsonValueKind.String && i.GetString()!.Trim().Length > 0).Select(i => i.GetString()!.Trim().ToLowerInvariant()).ToList();
        }
        catch (JsonException error)
        {
            Say($"{file} is not readable JSON: ignored ({error.Message}).", PackColor.Yellow);
            return [];
        }
    }

    public int Run(PackOptions options)
    {
        var only = Split(options.Only);
        var skip = Split(options.Skip);
        var productKeys = PackProduct.All.Select(p => p.Key).ToList();
        var aiKeys = AiNames.Keys.ToList();
        foreach (var name in LocalSkip())
        {
            if (!productKeys.Contains(name) && !aiKeys.Contains(name))
            {
                Say($"nexussystem.json: unknown name '{name}' ignored.", PackColor.Yellow);
                continue;
            }
            if (!skip.Contains(name)) skip.Add(name);
        }
        foreach (var name in only.Concat(skip))
        {
            if (!productKeys.Contains(name) && !aiKeys.Contains(name))
            {
                Say($"Unknown name '{name}'. Products: {string.Join(", ", productKeys)}. AI apps: {string.Join(", ", aiKeys)}.", PackColor.Red);
                return 2;
            }
        }

        Say("Nexus pack: computer use in the background for your AI apps (https://julienpiron.fr)", PackColor.Cyan);
        var found = services.FindAiApps().ToList();
        var ais = found.Where(ai => Wanted(ai, aiKeys, only, skip)).ToList();
        var wanted = PackProduct.All.Where(p => Wanted(p.Key, productKeys, only, skip)).ToList();
        var selected = wanted.Where(p => Unfit(p, ais) is null).ToList();
        if (found.Count == 0)
        {
            Say("No AI app found (Claude Code, Claude Desktop, Codex, Antigravity): install one, open it once, then run this again.", PackColor.Yellow);
            return 1;
        }
        Say();
        Say($"AI apps found: {string.Join(", ", found.Select(Name))}");
        services.Event(new Dictionary<string, object?> { ["type"] = "found", ["ais"] = found });
        void SayUnfit()
        {
            foreach (var product in wanted)
            {
                if (Unfit(product, ais) is { } why) Say($"  {product.Name,-13} {why}", PackColor.DarkGray);
            }
        }
        if (ais.Count == 0 || selected.Count == 0)
        {
            SayUnfit();
            Say("Nothing to install with these options.", PackColor.Yellow);
            return 0;
        }
        Say("It will install:");
        foreach (var product in selected)
        {
            var target = product.Device is not null ? $"your {product.Device}" : string.Join(", ", Cells(product, ais).Select(Name));
            Say($"  {product.Name,-13} for {target}");
        }
        SayUnfit();

        // The user's own choices: an AI app where a product is turned off stays as it is for that product.
        var kept = new HashSet<string>();
        HashSet<string> codexCustom = [], codexExisting = [], codexUnrecognized = [];
        foreach (var product in selected)
        {
            foreach (var ai in Cells(product, ais))
            {
                if (ai != "pc" && Check(product, ai) == "disabled") kept.Add($"{product.Key}|{ai}");
                if (ai != "codex" || product.Plugin is null || kept.Contains($"{product.Key}|{ai}")) continue;
                var config = CodexConfig();
                if (CodexToml.PluginState(config, product.Server!) == "unknown")
                {
                    kept.Add($"{product.Key}|{ai}");
                    codexUnrecognized.Add(product.Key);
                }
                else if (CodexToml.ServerTables(config, product.Server!).Count > 0 && !CodexToml.IsOwnedServer(config, product.Server!, Folders))
                {
                    kept.Add($"{product.Key}|{ai}");
                    codexCustom.Add(product.Key);
                }
                else if (CodexToml.PluginState(config, product.Server!) == "enabled" &&
                    CodexToml.PluginState(config, product.Server!, Declarations.Marketplace) != "enabled")
                {
                    kept.Add($"{product.Key}|{ai}");
                    codexExisting.Add(product.Key);
                }
            }
        }
        // Each one-run installer is told every chosen AI app, the plugin ones included (it sees the plugins and adds
        // no second server), since the plugins reach the engine or daemon it installs.
        var targets = new Dictionary<string, List<string>>();
        foreach (var product in selected.Where(p => OneRunForAll.Contains(p.Key)))
            targets[product.Key] = found.Where(ai => product.Targets.Contains(ai) && ais.Contains(ai) && !kept.Contains($"{product.Key}|{ai}")).ToList();
        foreach (var key in kept)
        {
            var parts = key.Split('|');
            var name = PackProduct.ByKey(parts[0]).Name;
            if (parts[1] == "codex" && codexUnrecognized.Contains(parts[0]))
                Say($"  {name,-13} Codex: unsupported plugin configuration kept; no second server or plugin added", PackColor.Yellow);
            else if (parts[1] == "codex" && codexCustom.Contains(parts[0]))
                Say($"  {name,-13} Codex: custom direct declaration kept; no second server or plugin added", PackColor.Yellow);
            else if (parts[1] == "codex" && codexExisting.Contains(parts[0]))
                Say($"  {name,-13} Codex: plugin from another catalog kept; no second plugin added", PackColor.Yellow);
            else Say($"  {name,-13} {Name(parts[1])}: turned off by you, left as it is", PackColor.Yellow);
        }

        var wants = selected.Select(p => p.Key).ToHashSet();
        var forAi = ais.ToHashSet();
        // Claude Code and Codex add a catalog by cloning it with git, which a new Windows lacks.
        var needsGit = (forAi.Contains("claude-code") && selected.Any(p => p.Plugin is not null && !kept.Contains($"{p.Key}|claude-code")) ||
            forAi.Contains("codex") && selected.Any(p => p.Plugin is not null && !kept.Contains($"{p.Key}|codex"))) && !services.HasGit();
        Say();
        var gitFor = string.Join(" and ", CatalogClients.Where(forAi.Contains).Select(Name));
        Say($"Prerequisites, once: {(needsGit ? $"Git (for {gitFor})" : "none")}");
        services.Event(new Dictionary<string, object?>
        {
            ["type"] = "plan", ["ais"] = ais, ["products"] = selected.Where(p => wants.Contains(p.Key)).Select(p => p.Key).ToList(),
            ["git"] = needsGit, ["installer_targets"] = targets,
        });
        if (options.DryRun)
        {
            Say("Dry run: nothing installed.", PackColor.Yellow);
            return 0;
        }

        var results = new Dictionary<string, int>(); // "product|ai": exit code of the route that serves it
        int? gitCode = null;
        var codexPlugins = selected.Where(p => p.Plugin is not null && forAi.Contains("codex") && !kept.Contains($"{p.Key}|codex")).ToList();
        var work = Path.Combine(Path.GetTempPath(), $"nexus-pack-{Environment.ProcessId}");
        Directory.CreateDirectory(work);
        var savedPack = Environment.GetEnvironmentVariable("NEXUS_PACK");
        var savedTargets = Environment.GetEnvironmentVariable("NEXUS_PACK_TARGETS");
        Environment.SetEnvironmentVariable("NEXUS_PACK", "1"); // the product installers' non-interactive mode: no question, no wait, no window
        try
        {
            if (needsGit) gitCode = services.InstallGit(this);

            // 1. The plugins first: the product installers see them and add no second server for Claude Code or Codex.
            if (forAi.Contains("claude-code"))
            {
                var withPlugin = selected.Where(p => p.Plugin is not null && !kept.Contains($"{p.Key}|claude-code")).ToList();
                if (withPlugin.Count > 0)
                {
                    var code = gitCode is > 0 ? 1 : services.InstallClaudePlugins(this, withPlugin.Select(p => p.Plugin!).ToList(), options.Catalog);
                    foreach (var product in withPlugin) results[$"{product.Key}|claude-code"] = code;
                }
            }
            // Codex gets the real plugin (MCP plus its guide); the final pass below removes only the exact direct
            // declarations an official installer wrote (today's or a former one), once the plugin is verified.
            if (codexPlugins.Count > 0)
            {
                var cli = services.CodexCli();
                var catalogCode = cli is null || gitCode is > 0 ? 1 : services.InstallCodexMarketplace(this, cli, options.Catalog);
                if (cli is null) Say("The installed Codex engine with a valid OpenAI signature was not found. Open or update Codex, then run this again.", PackColor.Red);
                foreach (var product in codexPlugins)
                    results[$"{product.Key}|codex"] = catalogCode != 0 ? 1 : services.InstallCodexPlugin(this, cli!, product.Plugin!);
            }
            // 2. Each product's own native installer, in this order: NexusWindows (engine at its stable path),
            //    NexusAndroid (daemon and adb), NexusBrowser (daemon and Chrome's native host), NexusRelay (daemon),
            //    NexusAGYmail (its Antigravity plugin), NexusInDesign, NexusFree. Each is told the AI apps chosen here;
            //    a failed installer fails every cell it serves, plugin cells included.
            foreach (var key in OneRunForAll)
            {
                if (!wants.Contains(key) || !targets.TryGetValue(key, out var chosen) || chosen.Count == 0) continue;
                Environment.SetEnvironmentVariable("NEXUS_PACK_TARGETS", string.Join(',', chosen));
                var code = services.RunProductInstaller(this, PackProduct.ByKey(key), work);
                foreach (var ai in chosen)
                {
                    if (results.GetValueOrDefault($"{key}|{ai}") == 0) results[$"{key}|{ai}"] = code;
                }
            }
            Environment.SetEnvironmentVariable("NEXUS_PACK_TARGETS", null);
            // 3. NexusKeypad: the full plugin for Logi Options+ (keys placed by a guide, below).
            if (wants.Contains("keypad")) results["keypad|pc"] = services.RunProductInstaller(this, PackProduct.ByKey("keypad"), work);
            foreach (var product in codexPlugins.Where(p => results[$"{p.Key}|codex"] == 0))
            {
                var code = Step($"Codex: verify {product.Plugin} guide and single server", () =>
                {
                    if (!PluginMaintenance.CodexPluginInstalled(product.Plugin!, Folders))
                        throw new InvalidOperationException("The installed Codex plugin or its guide is missing; the direct declaration was kept.");
                    if (CodexToml.RemoveOwnedServer(Folders, product.Server!))
                        Say($"{product.Server}: the plugin now provides its tools and guide; the old direct declaration was removed.");
                    return 0;
                });
                if (code != 0) results[$"{product.Key}|codex"] = code;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXUS_PACK", savedPack);
            Environment.SetEnvironmentVariable("NEXUS_PACK_TARGETS", savedTargets);
            try { Directory.Delete(work, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* a temporary folder of this run */ }
        }

        // A successful product installation also establishes the one shared update authority.
        var maintenanceFailed = false;
        if (results.Values.Any(code => code == 0))
        {
            try { services.InstallMaintenance(options); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                maintenanceFailed = true;
                Say($"Maintenance could not be registered: {error.Message}", PackColor.Red);
            }
        }

        // Summary: a cell is green when its route succeeded AND the AI app now declares the product.
        Say();
        Say("Summary", PackColor.Cyan);
        var failed = maintenanceFailed ? 1 : 0;
        if (gitCode is > 0)
        {
            Say($"  {No} git (the steps that need it were not run)", PackColor.Red);
            failed++;
        }
        const int width = 16;
        Say("  " + "".PadRight(14) + string.Concat(ais.Select(ai => Name(ai).PadRight(width))));
        var events = new List<Dictionary<string, object?>>();
        foreach (var product in selected)
        {
            services.Output.Write("  " + product.Name.PadRight(14));
            var cells = Cells(product, ais);
            foreach (var ai in product.Device is not null ? ["pc"] : ais)
            {
                if (!cells.Contains(ai))
                {
                    services.Output.Write("-".PadRight(width), PackColor.DarkGray);
                    continue;
                }
                Dictionary<string, object?> Cell(bool good, string state) =>
                    new() { ["type"] = "cell", ["product"] = product.Key, ["ai"] = ai, ["ok"] = good, ["state"] = state };
                if (kept.Contains($"{product.Key}|{ai}"))
                {
                    var codexKept = ai == "codex" && (codexCustom.Contains(product.Key) || codexExisting.Contains(product.Key) || codexUnrecognized.Contains(product.Key));
                    events.Add(Cell(false, codexKept ? "kept" : "disabled"));
                    services.Output.Write((codexKept ? "- kept" : "- off, kept").PadRight(width), PackColor.Yellow);
                    continue;
                }
                var label = product.Device ?? "";
                var state = Check(product, ai);
                var good = results.TryGetValue($"{product.Key}|{ai}", out var code) && code == 0 && state == "ok";
                if (!good) failed++;
                events.Add(Cell(good, good ? "ok" : state));
                if (good) services.Output.Write($"{Ok} ok {label}".PadRight(width), PackColor.Green);
                else if (state == "invalid") services.Output.Write($"{No} invalid".PadRight(width), PackColor.Red);
                else services.Output.Write($"{No} failed {label}".PadRight(width), PackColor.Red);
            }
            Say();
        }
        foreach (var e in events) services.Event(e); // after the table, each on its own line
        SayUnfit();

        Say();
        Say("Left to you", PackColor.Cyan);
        var step = 0;
        if (wants.Contains("browser"))
        {
            Say($"  {++step}. Chrome: load the NexusBrowser extension once (guide: https://github.com/pironjulien/NexusBrowser#install).");
            var guide = Path.Combine(Folders.LocalAppData, "NexusBrowser", "app", "scripts", "guide-extension.ps1");
            if (!options.NoGuide && File.Exists(guide)) services.ShowBrowserGuide(guide);
        }
        if (wants.Contains("android"))
        {
            Say($"  {++step}. Phone: Settings > About phone, tap Build number 7 times; Developer options > turn on USB debugging.");
            Say("     Plug it in with a USB data cable and accept this PC on the phone; the first connection is guided by");
            Say("     https://github.com/pironjulien/NexusAndroid#quick-start");
        }
        // The Codex project, only when Codex answers (Claude Code is the host); as the host, Codex needs none.
        if (wants.Contains("relay") && targets.TryGetValue("relay", out var relay) && relay.Contains("codex") && relay.Contains("claude-code"))
        {
            Say($"  {++step}. Codex, once: Add a project, name \"NexusRelay\", folder {Path.Combine(Folders.Home, ".nexus", "relay", "codex-projet")}");
            Say("     (https://github.com/pironjulien/NexusRelay#quick-start)");
        }
        if (wants.Contains("agymail"))
        {
            Say($"  {++step}. Gmail: create your own Google OAuth client (Desktop app), then sign in with it as shown by");
            Say("     https://github.com/pironjulien/NexusAGYmail#quick-start");
        }
        if (wants.Contains("keypad"))
        {
            Say($"  {++step}. MX Creative Keypad: in Logi Options+, place the NexusKeypad keys as the guide shows");
            Say("     (https://github.com/pironjulien/NexusKeypad#install)");
        }
        Say($"  {++step}. Restart your AI apps: quit them completely (also from the notification area), then open them again.");
        if (failed > 0)
        {
            Say();
            Say($"{failed} item(s) failed: read the messages above, then run the same command again.", PackColor.Red);
            return 1;
        }
        Say();
        var first = Examples.FirstOrDefault(e => wants.Contains(e.Key));
        Say(first.Text is null ? "Done." : $"Done. Ask your AI, for example: \"{first.Text}\"", PackColor.Green);
        return 0;
    }

    /// <summary>
    /// One step of the installation, announced, with its exit code kept: an exception is its message and exit code 1.
    /// </summary>
    public int Step(string what, Func<int> action)
    {
        Say();
        Say($"== {what}", PackColor.Cyan);
        services.Event(new Dictionary<string, object?> { ["type"] = "step", ["name"] = what, ["state"] = "start" });
        int code;
        try { code = action(); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say(error.Message, PackColor.Red);
            code = 1;
        }
        if (code != 0) Say($"{what} failed (exit code {code}).", PackColor.Red);
        services.Event(new Dictionary<string, object?> { ["type"] = "step", ["name"] = what, ["state"] = "end", ["code"] = code });
        return code;
    }

    /// <summary>A line of the installation, for the steps the services run.</summary>
    public void Message(string text = "", PackColor color = PackColor.Gray) => Say(text, color);

    /// <summary>One "NEXUS_PACK_EVENT {json}" line (compact JSON).</summary>
    public static string EventLine(IReadOnlyDictionary<string, object?> data)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteValue(json, data);
        return "NEXUS_PACK_EVENT " + Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteValue(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null: json.WriteNullValue(); break;
            case string text: json.WriteStringValue(text); break;
            case bool flag: json.WriteBooleanValue(flag); break;
            case int number: json.WriteNumberValue(number); break;
            case IReadOnlyDictionary<string, object?> map:
                json.WriteStartObject();
                foreach (var (key, item) in map) { json.WritePropertyName(key); WriteValue(json, item); }
                json.WriteEndObject();
                break;
            case IReadOnlyDictionary<string, List<string>> lists:
                json.WriteStartObject();
                foreach (var (key, item) in lists) { json.WritePropertyName(key); WriteValue(json, item); }
                json.WriteEndObject();
                break;
            case IEnumerable<string> items:
                json.WriteStartArray();
                foreach (var item in items) json.WriteStringValue(item);
                json.WriteEndArray();
                break;
            default: throw new ArgumentException($"Unsupported event value {value.GetType().Name}.", nameof(value));
        }
    }
}
