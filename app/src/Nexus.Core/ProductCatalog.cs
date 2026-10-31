using System.Text.Json;

namespace Nexus.Core;

/// <summary>A group of the app's menu and home page, with its title in French and English.</summary>
public sealed record ProductGroup(string Key, string Fr, string En);

/// <summary>The products the app shows, in their groups and in this order.</summary>
public sealed record ProductCatalogData(IReadOnlyList<ProductGroup> Groups, IReadOnlyList<Product> Products)
{
    public Product? ByKey(string key) => Products.FirstOrDefault(p => p.Key == key);
}

/// <summary>
/// The list of products the app shows, read from products.json at the root of the NexusSystem repository, so that
/// a product can be added, moved to another group, released from "coming soon" or given a store without a new
/// version of the app. The last copy read is kept in ~\.nexus\nexussystem\products.json for an offline start; the
/// products built into the app are the fallback. What only the code knows (how a product is installed and
/// recognized) stays in <see cref="Products"/>: the catalog changes how a known product is shown, and adds products
/// installed by a store, coming soon, or installed by get-nexus.ps1 (-Only key) through the MCP server it names.
/// </summary>
public static class ProductCatalog
{
    public const string Url = "https://raw.githubusercontent.com/pironjulien/NexusSystem/main/products.json";

    public static readonly IReadOnlyList<ProductGroup> BuiltInGroups =
    [
        new("ai", "Produits IA", "AI products"),
        new("adobe", "Produits Adobe", "Adobe products"),
        new("windows", "Produits Windows", "Windows products"),
    ];

    public static readonly ProductCatalogData BuiltIn = new(BuiltInGroups, Products.All);

    public static string CachePath(UserFolders folders) => Path.Combine(folders.Nexus, "nexussystem", "products.json");

    /// <summary>The last catalog read, or the built-in one.</summary>
    public static ProductCatalogData Load(UserFolders folders)
    {
        try
        {
            var file = CachePath(folders);
            return File.Exists(file) ? Parse(File.ReadAllText(file)) ?? BuiltIn : BuiltIn;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return BuiltIn; }
    }

    /// <summary>Reads the online catalog and keeps a copy; null when it cannot be read or is not valid.</summary>
    public static async Task<ProductCatalogData?> FetchAsync(HttpClient http, UserFolders folders, CancellationToken cancel = default)
    {
        try
        {
            var json = await http.GetStringAsync(Url, cancel);
            if (Parse(json) is not { } catalog) return null;
            var file = CachePath(folders);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, json, cancel);
            return catalog;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The catalog of this JSON, merged with what the code knows of each product; null when not valid.</summary>
    public static ProductCatalogData? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Number(root, "schema") != 1 ||
                !root.TryGetProperty("groups", out var groupsJson) || groupsJson.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("products", out var productsJson) || productsJson.ValueKind != JsonValueKind.Array) return null;
            var groups = groupsJson.EnumerateArray()
                .Select(g => (Key: Text(g, "key"), Fr: Text(g, "fr"), En: Text(g, "en")))
                .Where(g => g.Key is { Length: > 0 } && g.Fr is { Length: > 0 } && g.En is { Length: > 0 })
                .Select(g => new ProductGroup(g.Key!, g.Fr!, g.En!))
                .DistinctBy(g => g.Key).ToList();
            var products = productsJson.EnumerateArray().Select(entry => Product(entry, groups)).OfType<Product>().DistinctBy(p => p.Key).ToList();
            return groups.Count > 0 && products.Count > 0 ? new ProductCatalogData(groups.Where(g => products.Any(p => p.Group == g.Key)).ToList(), products) : null;
        }
        catch (JsonException) { return null; }
    }

    static Product? Product(JsonElement entry, IReadOnlyList<ProductGroup> groups)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        var key = Text(entry, "key");
        var group = Text(entry, "group");
        if (key is not { Length: > 0 } || !key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) || groups.All(g => g.Key != group)) return null;
        var known = Products.All.FirstOrDefault(p => p.Key == key);
        var server = known?.Server ?? Text(entry, "server");
        var availability = Text(entry, "state") switch
        {
            // Installed by the AI apps: a product the code knows, or one whose MCP server the pack installs.
            "ready" when known?.Availability == Availability.Ready || known is null && server is not null => Availability.Ready,
            "store" => Availability.OtherStore,
            "soon" => Availability.NotPublic,
            _ => known?.Availability,
        };
        if (availability is null) return null;
        var store = Address(Text(entry, "store"), "ms-windows-store", "https");
        if (availability == Availability.OtherStore && store is null) return null;
        var about = entry.TryGetProperty("about", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        var accent = Text(entry, "accent") is { Length: 7 } color && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit) ? color : null;
        var targets = known?.Targets ?? (entry.TryGetProperty("targets", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? AiApps.All.FirstOrDefault(app => AiApps.Key(app) == x.GetString(), (AiApp)(-1)) : (AiApp)(-1))
                .Where(app => app != (AiApp)(-1)).Distinct().ToList()
            : []);
        var name = Text(entry, "name") is { Length: > 0 } n ? n : known?.Name;
        if (name is null || (accent ?? known?.Accent) is null) return null;
        var basis = known ?? new Product(key, name, accent!, availability.Value, null, targets, Server: server,
            ClaudePlugin: Text(entry, "claudePlugin"), AntigravityPlugin: Text(entry, "antigravityPlugin"));
        return basis with
        {
            Name = name,
            Accent = accent ?? basis.Accent,
            Availability = availability.Value,
            Group = group!,
            Page = Address(Text(entry, "page"), "https") ?? basis.Page,
            StoreUrl = availability == Availability.OtherStore ? store : null,
            PackageFamily = Text(entry, "package") ?? basis.PackageFamily,
            Icon = Address(Text(entry, "icon"), "https"),
            Banner = Address(Text(entry, "banner"), "https"),
            AboutFr = about.ValueKind == JsonValueKind.Object ? Text(about, "fr") : null,
            AboutEn = about.ValueKind == JsonValueKind.Object ? Text(about, "en") : null,
            Repository = Text(entry, "repository") ?? basis.Repository,
        };
    }

    static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Trim().Length > 0 ? text.Trim() : null;

    static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    /// <summary>An absolute address with one of these schemes only (never a file, a script or a local program).</summary>
    static string? Address(string? text, params string[] schemes) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && schemes.Contains(uri.Scheme) ? uri.OriginalString : null;
}
