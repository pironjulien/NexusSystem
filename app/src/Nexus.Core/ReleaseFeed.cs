using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nexus.Core;

/// <summary>
/// The newest release a product offers on this PC's channel, read from GitHub without its API (no per-IP rate limit).
/// Stable: the release GitHub marks latest (releases/latest). Development: the newest entry of the releases feed,
/// a development build vX.Y.Z-dev.N or a stable release. The channel is "channel" in nexussystem.json ("dev");
/// anything else, or no file, is stable.
/// </summary>
public static class ReleaseFeed
{
    public const string Stable = "stable";
    public const string Development = "dev";

    static readonly Regex Tag = new(@"^v\d+\.\d+\.\d+(-dev\.\d+)?$");

    static readonly Dictionary<string, string> Repositories = new()
    {
        ["windows"] = "NexusWindows",
        ["android"] = "NexusAndroid",
        ["browser"] = "NexusBrowser",
        ["relay"] = "NexusRelay",
        ["agymail"] = "NexusAGYmail",
        ["keypad"] = "NexusKeypad",
    };

    public static string? Repository(Product product) => Repositories.GetValueOrDefault(product.Key);

    public static string Channel(UserFolders folders)
    {
        try
        {
            var file = LocalSettings.FilePath(folders);
            if (!File.Exists(file)) return Stable;
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("channel", out var channel) &&
                channel.ValueKind == JsonValueKind.String && channel.GetString() == Development ? Development : Stable;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Stable;
        }
    }

    /// <summary>The newest release tag of the channel, or null when GitHub does not answer.</summary>
    public static async Task<string?> NewestAsync(string repository, string channel, CancellationToken cancel = default)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusSystem");
            if (channel == Development)
                return NewestInFeed(await http.GetStringAsync(new Uri($"https://github.com/pironjulien/{repository}/releases.atom"), cancel));
            using var response = await http.GetAsync(new Uri($"https://github.com/pironjulien/{repository}/releases/latest"), cancel);
            var location = response.Headers.Location?.ToString();
            var tag = location?[(location.LastIndexOf('/') + 1)..];
            return tag is not null && Tag.IsMatch(tag) ? tag : null;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The first release tag of a GitHub releases feed (newest first).</summary>
    public static string? NewestInFeed(string atom)
    {
        XNamespace ns = "http://www.w3.org/2005/Atom";
        return XDocument.Parse(atom).Root?.Elements(ns + "entry")
            .Select(entry => entry.Elements(ns + "link").Select(link => (string?)link.Attribute("href")).FirstOrDefault(href => href?.Contains("/releases/tag/") == true))
            .Select(href => href?[(href.LastIndexOf('/') + 1)..])
            .FirstOrDefault(tag => tag is not null && Tag.IsMatch(tag));
    }

    /// <summary>The version a tag carries, development suffix included (v0.22.7-dev.9 → 0.22.7-dev.9).</summary>
    public static string Version(string tag) => tag.TrimStart('v');

    /// <summary>A higher x.y.z than the installed version. A development build carries the x.y.z it will be promoted
    /// as, which is also what its engine reports once installed.</summary>
    public static bool IsNewer(string tag, string installed) => ProductState.CompareVersions(Version(tag), installed) > 0;
}
