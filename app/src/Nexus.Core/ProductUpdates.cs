using System.IO.Pipes;
using System.Net;
using System.Text.Json;

namespace Nexus.Core;

/// <summary>A product's own state file, ~\.nexus\status\&lt;name&gt;.json, written by the product itself.</summary>
public sealed record ProductState(string Name, string? Version, string? Latest, string State, DateTimeOffset? CheckedAt, string? Message)
{
    /// <summary>The products check for updates daily. A saved version is never proof of a running service.</summary>
    public bool IsFresh(DateTimeOffset now) => CheckedAt is { } at && at <= now && now - at <= TimeSpan.FromDays(1);

    /// <summary>
    /// Only a newer published version is an update: a product running ahead of its last daily check (released since,
    /// or built from the sources) is up to date, not shown as failing.
    /// </summary>
    public bool UpdateAvailable => Latest is not null && Version is not null && CompareVersions(Latest, Version) > 0 && State is not "updating";

    /// <summary>-1, 0 or 1 on the numeric x.y.z parts; a pre-release or build suffix counts as its version.</summary>
    public static int CompareVersions(string a, string b)
    {
        static int[] Parts(string version) => version.Split('-', '+')[0].Split('.')
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsAsciiDigit).ToArray()), out var number) ? number : 0)
            .ToArray();
        var (x, y) = (Parts(a), Parts(b));
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var (left, right) = (i < x.Length ? x[i] : 0, i < y.Length ? y[i] : 0);
            if (left != right) return left < right ? -1 : 1;
        }
        return 0;
    }
}

public enum UpdateRequest
{
    /// <summary>The product started its own update (it reports its progress in its state file).</summary>
    Started,
    UpToDate,
    /// <summary>An install that does not update itself (a git clone, a build from the sources).</summary>
    NotSelfUpdating,
    /// <summary>The product's process does not answer (not running, or too old to update itself).</summary>
    Unreachable,
}

/// <summary>
/// The hub requests the installed NexusSystem maintenance task when available for Browser and Windows.
/// Older installations retain the product-owned update interfaces. The hub never executes an installer.
/// </summary>
public static class ProductUpdates
{
    /// <summary>Legacy edition states remain readable for installation detection.</summary>
    public static IReadOnlyList<string> StateNames(Product product) => product.Key switch
    {
        "windows" => ["windows", "windows-claude", "windows-codex", "windows-antigravity"],
        _ => [product.Key],
    };

    public static IReadOnlyList<ProductState> Read(Product product, UserFolders folders) =>
        StateNames(product).Select(name => ReadOne(name, folders)).OfType<ProductState>().ToList();

    static ProductState? ReadOne(string name, UserFolders folders)
    {
        var path = Path.Combine(folders.Nexus, "status", name + ".json");
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var checkedAt = DateTimeOffset.TryParse(Text("checked_at") ?? Text("updatedAt"), out var at) ? at : (DateTimeOffset?)null;
            return new ProductState(name, Text("version"), Text("latest"), Text("state") ?? "unknown", checkedAt, Text("message"));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;  // being rewritten by the product: read again at the next refresh
        }
    }

    /// <summary>Installed already: it reports a state, or an AI app here declares it, or its device plugin is there.</summary>
    public static bool IsInstalled(Product product, IReadOnlyList<AiApp> found, UserFolders folders) =>
        Products.StoreInstalled(product, folders) ||
        (product == Products.Windows && WindowsRuntime.ReadInstallation(folders) is not null) ||
        Read(product, folders).Count > 0 || Declarations.DeviceInstalled(product, folders) ||
        found.Where(product.Targets.Contains).Any(app => Declarations.IsDeclared(product, app, folders));

    /// <summary>The installed version as maintenance, the shared Windows engine or the product itself records it, or null.</summary>
    public static string? InstalledVersion(Product product, UserFolders folders) =>
        MaintenanceUpdates.Read(folders)?.Products.FirstOrDefault(item => item.Key == product.Key)?.InstalledVersion
        ?? (product == Products.Windows ? WindowsRuntime.ReadInstallation(folders)?.Version : null)
        ?? Read(product, folders).Select(state => state.Version).FirstOrDefault(version => version is not null);

    /// <summary>Whether an installed maintenance worker or a product-owned interface can accept a request.</summary>
    public static bool CanRequest(Product product, UserFolders folders) =>
        MaintenanceUpdates.Supports(product) && MaintenanceUpdates.IsInstalled(folders) || product.Key switch
    {
        "relay" => Environment.GetEnvironmentVariable("NEXUS_MCP_TOKEN") is { Length: > 0 } || UserToken() is not null,
        "browser" => File.Exists(BrowserBridge.TokenFile(folders)),
        "keypad" => Directory.Exists(Path.Combine(folders.LocalAppData, "Logi", "LogiPluginService", "Plugins", "NexusKeypad")),
        _ => false,
    };

    /// <summary>Prefer coordinated maintenance without changing the compatibility API used by older products.</summary>
    public static Task<UpdateRequest> RequestManagedAsync(Product product, UserFolders folders, HttpClient http, CancellationToken cancel = default) =>
        MaintenanceUpdates.Supports(product) && MaintenanceUpdates.IsInstalled(folders)
            ? MaintenanceUpdates.RequestAsync(cancel) : RequestAsync(product, http, cancel);

    public static Task<UpdateRequest> RequestAsync(Product product, HttpClient http, CancellationToken cancel = default) => product.Key switch
    {
        "relay" => RelayAsync(http, cancel),
        "browser" => BrowserAsync(http, cancel),
        "keypad" => KeypadAsync(cancel),
        _ => Task.FromResult(UpdateRequest.Unreachable),
    };

    /// <summary>NexusRelay's daemon API (tests point it at a stand-in).</summary>
    public static string RelayApi { get; set; } = "http://127.0.0.1:16181";

    /// <summary>NexusKeypad's pipe (tests point it at a stand-in).</summary>
    public static string KeypadPipe { get; set; } = "nexus-trinite-pad";

    // NexusRelay 1.3.1: 202 started (or already running), 200 up to date, 409 an install that does not update itself.
    static async Task<UpdateRequest> RelayAsync(HttpClient http, CancellationToken cancel)
    {
        var token = Environment.GetEnvironmentVariable("NEXUS_MCP_TOKEN") is { Length: > 0 } t ? t : UserToken();
        if (token is null) return UpdateRequest.Unreachable;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(RelayApi + "/api/update"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent("");
            using var response = await http.SendAsync(request, cancel);
            return Answer(response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return UpdateRequest.Unreachable;
        }
    }

    // NexusBrowser 2.26.0: the bridge starts its own task "Nexus Browser Update"; same answers as NexusRelay's daemon.
    static async Task<UpdateRequest> BrowserAsync(HttpClient http, CancellationToken cancel)
    {
        if (BrowserBridge.Token(UserFolders.FromEnvironment()) is not { } token) return UpdateRequest.Unreachable;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BrowserBridge.Api + "/api/update"));
            request.Headers.Add("X-Nexus-Browser-Token", token);
            request.Content = new StringContent("");
            using var response = await http.SendAsync(request, cancel);
            return Answer(response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return UpdateRequest.Unreachable;
        }
    }

    static UpdateRequest Answer(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Accepted => UpdateRequest.Started,
        HttpStatusCode.OK => UpdateRequest.UpToDate,
        HttpStatusCode.Conflict => UpdateRequest.NotSelfUpdating,
        _ => UpdateRequest.Unreachable,
    };

    // The installer sets NEXUS_MCP_TOKEN as a user variable: an app started before it does not have it yet.
    static string? UserToken() => Environment.GetEnvironmentVariable("NEXUS_MCP_TOKEN", EnvironmentVariableTarget.User) is { Length: > 0 } token ? token : null;

    // NexusKeypad's complete edition: one command per connection, one line back ("started" or "already running").
    // Only "update" is ever sent: the pipe's other commands drive the pad.
    static async Task<UpdateRequest> KeypadAsync(CancellationToken cancel)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", KeypadPipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(3000, cancel);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("update".AsMemory(), cancel);
            var answer = (await reader.ReadLineAsync(cancel) ?? "").Trim();
            return answer is "started" or "already running" ? UpdateRequest.Started : UpdateRequest.Unreachable;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return UpdateRequest.Unreachable;
        }
    }
}
