using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Win32;

namespace Nexus.Core;

public enum BridgeState
{
    /// <summary>No bridge token: NexusBrowser is not installed.</summary>
    NotInstalled,
    /// <summary>The bridge does not answer on 127.0.0.1:9225 (its task "Nexus Browser Bridge" starts it at sign-in).</summary>
    Offline,
    /// <summary>The bridge answers, but Chrome itself is closed. This is idle, not a pairing failure.</summary>
    BrowserClosed,
    /// <summary>The bridge runs, the Chrome extension is not connected (not loaded, or not paired).</summary>
    Waiting,
    Connected,
}

/// <summary>
/// The NexusBrowser bridge as the app sees it, natively (no PowerShell): its state, and the pairing that replaces the
/// native host where Chrome cannot see it (NexusBrowser 2.25.0, scripts/pair-extension.ps1 does the same). From the
/// Store package the native host's HKCU key stays in the package; the pairing needs none.
/// </summary>
public static class BrowserBridge
{
    /// <summary>The bridge's HTTP API (tests point it at a stand-in).</summary>
    public static string Api { get; set; } = "http://127.0.0.1:9225";
    const string TokenHeader = "X-Nexus-Browser-Token";

    public static string TokenFile(UserFolders folders) =>
        Environment.GetEnvironmentVariable("NEXUS_BROWSER_TOKEN_FILE") ?? Path.Combine(folders.Nexus, "nexus-browser", "bridge-token");

    public static string? Token(UserFolders folders) => File.Exists(TokenFile(folders)) ? File.ReadAllText(TokenFile(folders)).Trim() : null;

    public static async Task<BridgeState> StateAsync(UserFolders folders, HttpClient http, CancellationToken cancel = default)
    {
        if (Token(folders) is not { } token) return BridgeState.NotInstalled;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Api + "/api/status"));
            request.Headers.Add(TokenHeader, token);
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode) return BridgeState.Offline;
            using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            if (!status.RootElement.TryGetProperty("connected_clients", out var clients) || clients.ValueKind != JsonValueKind.Number ||
                !clients.TryGetInt32(out var count) || count < 0) return BridgeState.Offline;
            if (count > 0) return BridgeState.Connected;
            var running = status.RootElement.TryGetProperty("chrome_running", out var chrome) &&
                chrome.ValueKind is JsonValueKind.True or JsonValueKind.False ? chrome.GetBoolean() : ChromeRunning();
            return running ? BridgeState.Waiting : BridgeState.BrowserClosed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return BridgeState.Offline;
        }
    }

    /// <summary>
    /// A single-use pairing secret (256 bits, 5 minutes), or null when the bridge is older than NexusBrowser 2.25.0
    /// (no pairing: the guided fallback applies).
    /// </summary>
    public static async Task<string?> OfferAsync(UserFolders folders, HttpClient http, CancellationToken cancel = default)
    {
        var token = Token(folders) ?? throw new InvalidOperationException("NexusBrowser is not installed.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Api + "/api/pair"));
        request.Headers.Add(TokenHeader, token);
        request.Content = new StringContent("");
        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var offer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return offer.RootElement.GetProperty("secret").GetString();
    }

    /// <summary>The extension's ID from the installed bridge configuration: the Chrome Web Store one once public.</summary>
    public static string? ExtensionId(UserFolders folders)
    {
        var path = Path.Combine(folders.LocalAppData, "NexusBrowser", "app", "native-host", "bridge_config.json");
        if (!File.Exists(path)) return null;
        using var config = JsonDocument.Parse(File.ReadAllText(path));
        var root = config.RootElement;
        if (root.TryGetProperty("store_public", out var isPublic) && isPublic.ValueKind == JsonValueKind.True)
            return root.GetProperty("store_id").GetString();
        var origin = root.GetProperty("allowed_origins")[0].GetString() ?? "";
        return origin.Replace("chrome-extension://", "", StringComparison.Ordinal).TrimEnd('/');
    }

    public static string? ChromePath()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key?.GetValue(null) is string path && File.Exists(path)) return path;
        }
        return null;
    }

    public static bool ChromeRunning() => Process.GetProcessesByName("chrome").Length > 0;

    /// <summary>
    /// Opens the extension's pairing page in the running Chrome. Chrome must already run: a chrome.exe started from
    /// the package only hands the address to the running browser, and a browser started from the package would keep
    /// its profile changes in the package's store.
    /// </summary>
    public static void OpenPairingPage(string chrome, string extensionId, string secret)
    {
        var start = new ProcessStartInfo(chrome) { UseShellExecute = false };
        start.ArgumentList.Add($"chrome-extension://{extensionId}/pair.html#{secret}");
        Process.Start(start)?.Dispose();
    }
}
