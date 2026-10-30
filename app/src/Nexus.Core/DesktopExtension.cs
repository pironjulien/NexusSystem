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

    public string File(UserFolders folders) => Path.Combine(Folder(folders), Name + ".mcpb");

    /// <summary>Installed in Claude Desktop: a folder of its "Claude Extensions" whose manifest.json has this name.</summary>
    public bool IsInstalled(UserFolders folders) => AiApps.DesktopConfigFolders(folders).Any(config =>
    {
        var root = Path.Combine(config, "Claude Extensions");
        return Directory.Exists(root) && Directory.EnumerateDirectories(root).Any(d => ManifestName(Path.Combine(d, "manifest.json")) == Name);
    });

    /// <summary>The latest release's extension, saved under ~\.nexus\claude-desktop; returns its path.</summary>
    public async Task<string> DownloadAsync(UserFolders folders, HttpClient http, CancellationToken cancel = default)
    {
        var asset = Versioned ? $"{Name}-{await LatestVersionAsync(cancel)}.mcpb" : Name + ".mcpb";
        var bytes = await http.GetByteArrayAsync(new Uri($"https://github.com/pironjulien/{Repo}/releases/latest/download/{asset}"), cancel);
        Directory.CreateDirectory(Folder(folders));
        await System.IO.File.WriteAllBytesAsync(File(folders), bytes, cancel);
        return File(folders);
    }

    // The latest tag from the redirect of releases/latest (no GitHub API: its rate limit is per IP address).
    async Task<string> LatestVersionAsync(CancellationToken cancel)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);
        using var response = await http.GetAsync(new Uri($"https://github.com/pironjulien/{Repo}/releases/latest"), cancel);
        var location = response.Headers.Location?.ToString() ?? throw new InvalidOperationException($"No latest release for {Repo}.");
        return location[(location.LastIndexOf('/') + 1)..].TrimStart('v');
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
