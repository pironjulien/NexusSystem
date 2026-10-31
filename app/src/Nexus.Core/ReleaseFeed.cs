using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>
/// The newest release of a product, read from GitHub without its API (no per-IP rate limit): the release GitHub marks
/// latest (the redirect of releases/latest).
/// </summary>
public static class ReleaseFeed
{
    static readonly Regex Tag = new(@"^v\d+\.\d+\.\d+$");

    static readonly Dictionary<string, string> Repositories = new()
    {
        ["windows"] = "NexusWindows",
        ["android"] = "NexusAndroid",
        ["browser"] = "NexusBrowser",
        ["relay"] = "NexusRelay",
        ["agymail"] = "NexusAGYmail",
        ["keypad"] = "NexusKeypad",
    };

    public static string? Repository(Product product) => product.Repository ?? Repositories.GetValueOrDefault(product.Key);

    /// <summary>The latest release tag, or null when GitHub does not answer.</summary>
    public static async Task<string?> NewestAsync(string repository, CancellationToken cancel = default)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusSystem");
            using var response = await http.GetAsync(new Uri($"https://github.com/pironjulien/{repository}/releases/latest"), cancel);
            return TagOf(response.Headers.Location?.ToString());
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The release tag a releases/latest redirect points to, or null.</summary>
    public static string? TagOf(string? location)
    {
        var tag = location?[(location.LastIndexOf('/') + 1)..];
        return tag is not null && Tag.IsMatch(tag) ? tag : null;
    }

    /// <summary>The version a tag carries (v0.22.7 → 0.22.7).</summary>
    public static string Version(string tag) => tag.TrimStart('v');

    /// <summary>A higher x.y.z than the installed version.</summary>
    public static bool IsNewer(string tag, string installed) => ProductState.CompareVersions(Version(tag), installed) > 0;
}
