namespace Nexus.Core;

/// <summary>How a product reaches the user from the app.</summary>
public enum Availability
{
    /// <summary>Installed by the app (through the one-line install's own script).</summary>
    Ready,
    /// <summary>Public, installed by the one-line install for now; the app takes it in a later milestone.</summary>
    Later,
    /// <summary>Through a store only (the Microsoft Store for a Windows app): the app opens its listing.</summary>
    OtherStore,
    /// <summary>Not public yet.</summary>
    NotPublic,
}

/// <summary>
/// A Nexus product as the apps show it: name, color and icon of the julienpiron.fr site, its MCP server, the AI apps
/// it serves (none for a device), and what it needs on the PC. Same facts as the product table of get-nexus.ps1.
/// </summary>
public sealed record Product(
    string Key,
    string Name,
    string Accent,
    Availability Availability,
    string? Page,
    IReadOnlyList<AiApp> Targets,
    string? Server = null,
    string? ClaudePlugin = null,
    string? AntigravityPlugin = null,
    string? JournalFolder = null,
    AiApp? Needs = null,
    int MinAis = 0,
    string? Device = null,
    string? StoreUrl = null,
    // Former names of the Antigravity plugin, still recognised while public installations carry them.
    IReadOnlyList<string>? AntigravityFormerPlugins = null,
    // The group of the app's menu and home page (ProductCatalog's groups: ai, adobe, windows, or one the online catalog adds).
    string Group = "ai",
    // A store app's package family: installed when Windows has its folder under %LOCALAPPDATA%\Packages.
    string? PackageFamily = null,
    // From the online catalog: its icon on the site (a product the app has no picture of), its short text, its repository.
    string? Icon = null,
    string? AboutFr = null,
    string? AboutEn = null,
    string? Repository = null,
    // From the online catalog: the picture of its page (its video's poster on the site), and its video.
    string? Banner = null,
    string? Video = null)
{
    // A product is its key: the online catalog's copy of a product (other text, group or picture) is the same product,
    // so the code's checks (product == Products.Windows...) hold for it.
    public bool Equals(Product? other) => other is not null && Key == other.Key;

    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);

    /// <summary>The product's page on julienpiron.fr in the app's language (the English site lives under /en/).</summary>
    public string? PageFor(bool french) => Page is null || french ? Page : Page.Replace("julienpiron.fr/", "julienpiron.fr/en/", StringComparison.Ordinal);
}

public static class Products
{
    static readonly IReadOnlyList<AiApp> AllAi = AiApps.All;

    public static readonly Product Windows = new("windows", "NexusWindows", "#6cb6ff", Availability.Ready,
        "https://julienpiron.fr/nexuswindows/", AllAi, Server: "nexus-windows", ClaudePlugin: "nexus-windows",
        AntigravityPlugin: "nexus-windows", JournalFolder: "windows", AntigravityFormerPlugins: ["nexus-computer"]);

    public static readonly Product Android = new("android", "NexusAndroid", "#7ee0a1", Availability.Ready,
        "https://julienpiron.fr/nexusandroid/", AllAi, Server: "nexus-android", ClaudePlugin: "nexus-android",
        AntigravityPlugin: "nexus-android", JournalFolder: "android");

    public static readonly Product Browser = new("browser", "NexusBrowser", "#b9a2ff", Availability.Ready,
        "https://julienpiron.fr/nexusbrowser/", AllAi, Server: "nexus-browser", ClaudePlugin: "nexus-browser",
        AntigravityPlugin: "nexus-browser", JournalFolder: "browser");

    // The full plugin for Logi Options+, installed by the pack when Logi Options+ is there: one way per product, and the
    // Logitech Marketplace editions are trimmed to its rules.
    public static readonly Product Keypad = new("keypad", "NexusKeypad", "#f78fb3", Availability.Ready,
        "https://julienpiron.fr/nexuskeypad/", [], Device: "Logi Options+");

    // Two AI apps talking: one host opens the exchanges (NexusRelay 1.4.0: the first of Claude Code, Codex, Antigravity
    // here), the others answer. Claude Desktop's chat is not a relay client.
    public static readonly Product Relay = new("relay", "NexusRelay", "#a2fff0", Availability.Ready,
        "https://julienpiron.fr/nexusrelay/", [AiApp.ClaudeCode, AiApp.Codex, AiApp.Antigravity], Server: "nexus-relay",
        AntigravityPlugin: "nexus-relay", JournalFolder: "relay", MinAis: 2);

    // Claude and Codex have their own official Gmail connector.
    public static readonly Product AgyMail = new("agymail", "NexusAGYmail", "#ffb5a2", Availability.Ready,
        "https://julienpiron.fr/nexusagymail/", [AiApp.Antigravity], Server: "nexus-agymail", AntigravityPlugin: "nexus-agymail");

    // Adobe InDesign driven by the AI apps: still private, shown as coming soon.
    public static readonly Product InDesign = new("indesign", "NexusInDesign", "#e749a0", Availability.NotPublic,
        null, [], Group: "adobe");

    // A live hardware dashboard as the Windows wallpaper: not an AI tool, published on the Microsoft Store.
    public static readonly Product Wpp = new("wpp", "NexusWpp", "#53fff4", Availability.OtherStore,
        "https://julienpiron.fr/nexuswpp/", [], StoreUrl: "ms-windows-store://pdp/?productid=9N2VCVDH7QDX",
        Group: "windows", PackageFamily: "julienpiron.fr.NexusWpp_yq2nr3sn86fpg");

    public static readonly IReadOnlyList<Product> All = [Windows, Android, Browser, Keypad, Relay, AgyMail, InDesign, Wpp];

    /// <summary>A store app this user has installed.</summary>
    public static bool StoreInstalled(Product product, UserFolders folders) =>
        product.PackageFamily is { } family && Directory.Exists(Path.Combine(folders.LocalAppData, "Packages", family));

    public static Product ByKey(string key) => All.First(p => p.Key == key);

    /// <summary>The Logitech plugin service's folder (Logi Options+ opened once), where NexusKeypad goes.</summary>
    public static bool LogiService(UserFolders folders) => Directory.Exists(Path.Combine(folders.LocalAppData, "Logi", "LogiPluginService"));

    /// <summary>Why a product does not fit this PC (same reasons as get-nexus.ps1), or null when it does.</summary>
    public static Unfit? Why(Product product, IReadOnlyList<AiApp> found, UserFolders folders)
    {
        if (product.Device is not null) return LogiService(folders) ? null : Unfit.NeedsDevice;
        if (product.Needs is { } needed && !found.Contains(needed)) return Unfit.NeedsApp;
        if (product.MinAis > 0 && product.Targets.Count(found.Contains) < product.MinAis) return Unfit.NeedsTwo;
        return product.Targets.Any(found.Contains) ? null : Unfit.NoTarget;
    }

    /// <summary>
    /// Builds the prompt the user can paste into Claude Code, Codex or Antigravity to install or repair the selected
    /// products through the same official pack script (get-nexus.ps1 -Only ...) and INSTALL-AI.md guide. The products
    /// are those shown (<paramref name="among"/>: the online catalog's), the built-in ones by default.
    /// </summary>
    public static string AiPrompt(IEnumerable<string> chosenKeys, bool french = false, IReadOnlyList<Product>? among = null)
    {
        var chosen = chosenKeys.Select(k => k.Trim().ToLowerInvariant()).ToHashSet();
        var ordered = (among ?? All).Where(p => p.Availability == Availability.Ready && chosen.Contains(p.Key)).ToList();
        if (ordered.Count == 0) return "";
        // Put windows before browser when both are selected so desktop tools are set up first.
        var keys = string.Join(",", ordered.Select(p => p.Key));
        var names = string.Join(", ", ordered.Select(p => p.Name));
        const string guide = "https://raw.githubusercontent.com/pironjulien/NexusSystem/main/INSTALL-AI.md";
        // The pack script as a checked file of the latest release, run with -File (never downloaded code in memory).
        var command = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\get-nexus.ps1 -Only {keys} -NoGuide";
        return french
            ? $"Installe et configure pour moi {names} en suivant {guide} : télécharge get-nexus.ps1 en fichier depuis la dernière release, vérifie son empreinte, puis lance : {command}"
            : $"Install and set up {names} for me by following {guide}: download get-nexus.ps1 as a file from the latest release, check its SHA-256, then run: {command}";
    }
}

public enum Unfit
{
    NeedsDevice,
    NeedsApp,
    /// <summary>Two AI apps among its targets (NexusRelay: one host, one that answers).</summary>
    NeedsTwo,
    NoTarget,
}
