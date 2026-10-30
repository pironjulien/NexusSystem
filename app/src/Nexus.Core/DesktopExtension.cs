using System.Text.Json;

namespace Nexus.Core;

/// <summary>
/// A product's Claude Desktop extension (.mcpb), attached to each of its releases. The app downloads it into
/// ~\.nexus\claude-desktop (outside AppData: a real file, even from the Store package) and the user drops it on Claude
/// Desktop, which shows its own confirmation. Claude Desktop declares no .mcpb file association: opening the file would
/// only ask Windows which app to use.
/// </summary>
public sealed record DesktopExtension(Product Product, string Repo, string Name, bool Versioned)
{
    public static DesktopExtension? For(Product product) => product.Key switch
    {
        // The single NexusWindows repository attaches the Claude profile's extension as nexus-windows-claude.mcpb.
        "windows" => new(product, "NexusWindows", "nexus-windows-claude", Versioned: false),
        "android" => new(product, "NexusAndroid", "nexus-android", Versioned: false),
        "browser" => new(product, "NexusBrowser", "nexus-browser", Versioned: false),
        _ => null,
    };

    public static string Folder(UserFolders folders) => Path.Combine(folders.Nexus, "claude-desktop");

    /// <summary>
    /// Offered when Claude Desktop is on this PC, unless this PC's settings skip Claude Desktop or the product
    /// (%USERPROFILE%\.nexus\nexussystem.json, LocalSettings).
    /// </summary>
    public bool Offered(UserFolders folders, LocalSettings settings) =>
        !settings.Skips("claude-desktop") && !settings.Skips(Product.Key) && AiApps.DesktopConfigFolders(folders).Count > 0;

    public string File(UserFolders folders) => Path.Combine(Folder(folders), Name + ".mcpb");

    /// <summary>Installed in Claude Desktop: a folder of its "Claude Extensions" whose manifest.json has this name.</summary>
    public bool IsInstalled(UserFolders folders) => AiApps.DesktopConfigFolders(folders).Any(config =>
    {
        var root = Path.Combine(config, "Claude Extensions");
        return Directory.Exists(root) && Directory.EnumerateDirectories(root).Any(d => ManifestName(Path.Combine(d, "manifest.json")) == Name);
    });

    /// <summary>
    /// The latest release's extension, saved under ~\.nexus\claude-desktop; returns its path. When the release's
    /// SHA256SUMS.txt lists it, the bytes must match before anything is written.
    /// </summary>
    public async Task<string> DownloadAsync(UserFolders folders, HttpClient http, CancellationToken cancel = default)
    {
        var tag = await LatestTagAsync(cancel);
        var asset = Versioned ? $"{Name}-{tag.TrimStart('v')}.mcpb" : Name + ".mcpb";
        var release = $"https://github.com/pironjulien/{Repo}/releases/download/{tag}";
        var bytes = await http.GetByteArrayAsync(new Uri($"{release}/{asset}"), cancel);
        string? sums = null;
        try { sums = await http.GetStringAsync(new Uri($"{release}/SHA256SUMS.txt"), cancel); }
        catch (HttpRequestException) { /* a release without SHA256SUMS.txt */ }
        if (sums is not null && ExpectedSha256(sums, asset) is { } expected &&
            !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{asset} does not match its release's SHA256SUMS.txt.");
        Directory.CreateDirectory(Folder(folders));
        await System.IO.File.WriteAllBytesAsync(File(folders), bytes, cancel);
        return File(folders);
    }

    /// <summary>The hash a SHA256SUMS.txt gives for a file name, or null when it does not list it.</summary>
    public static string? ExpectedSha256(string sums, string name) => sums.Split('\n')
        .Select(line => System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(\S+)$"))
        .Where(m => m.Success && m.Groups[2].Value == name).Select(m => m.Groups[1].Value).FirstOrDefault();

    // The latest tag from the redirect of releases/latest (no GitHub API: its rate limit is per IP address).
    async Task<string> LatestTagAsync(CancellationToken cancel)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);
        using var response = await http.GetAsync(new Uri($"https://github.com/pironjulien/{Repo}/releases/latest"), cancel);
        var location = response.Headers.Location?.ToString() ?? throw new InvalidOperationException($"No latest release for {Repo}.");
        return location[(location.LastIndexOf('/') + 1)..];
    }

    static string? ManifestName(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            return document.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
