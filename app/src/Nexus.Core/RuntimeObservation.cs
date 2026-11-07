using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>
/// Which engine versions the AI connections really run: a NexusWindows engine counts only through its own runtime
/// receipt (process birth, loaded assembly bytes) under its real AI host; NexusBrowser through its live daemon, bridge
/// and extension. Installed files and caches are never taken for a running connection.
/// </summary>
public static partial class RuntimeObservation
{
    [GeneratedRegex(@"\s--mcp\s+--agent\s+(claude|codex|antigravity)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AgentProfile();

    [GeneratedRegex(@"(?:^|\s)exec(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CodexExec();

    [GeneratedRegex(@"(?:^|\s)--type(?:\s|=)", RegexOptions.IgnoreCase)]
    private static partial Regex ElectronChild();

    [GeneratedRegex(@"\\Claude-Data\\anthropicpbc\.claude_[a-z0-9]+\\claude-code\\\d+\.\d+\.\d+\\[a-f0-9]{12,64}\\claude\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex DesktopCodeEngine();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex Semantic();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.IgnoreCase)]
    private static partial Regex Hex64();

    public static LoadedEngines Observe(MaintenanceProduct product, UserFolders folders, IReadOnlyList<ProcessInfo> processes) => product.Key switch
    {
        "windows" => Windows(folders, processes),
        "browser" => Browser(folders, processes),
        _ => new LoadedEngines([], false, []),
    };

    /// <summary>
    /// The AI host that owns an engine process: its direct parent, through at most the inert stable launcher and the
    /// Codex catalogue's one cmd.exe hop. A qualification client below an AI's shell is deliberately not that AI's connection.
    /// </summary>
    public static string? Agent(ProcessInfo process, IReadOnlyDictionary<int, ProcessInfo> table, UserFolders folders)
    {
        if (AgentProfile().Match(process.CommandLine) is not { Success: true } profile) return null;
        var expected = profile.Groups[1].Value.ToLowerInvariant();
        var stable = Path.Combine(folders.Home, ".nexus", "nexus-windows", "NexusWindows.exe");
        var parent = table.GetValueOrDefault(process.ParentProcessId);
        if (parent is not null && parent.Name.Equals("NexusWindows.exe", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parent.ExecutablePath, stable, StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(parent.CommandLine, @"\s--mcp\s+--agent\s+" + Regex.Escape(expected) + @"\s*$", RegexOptions.IgnoreCase))
            parent = table.GetValueOrDefault(parent.ParentProcessId);
        if (parent is not null && expected == "codex" && Servers.IsCodexLauncher(new ServerProcess(parent.ParentProcessId, parent.Name, parent.CommandLine)))
            parent = table.GetValueOrDefault(parent.ParentProcessId);
        if (parent is null) return null;
        var name = parent.Name.ToLowerInvariant();
        if (expected == "codex" && name == "codex.exe" && !CodexExec().IsMatch(parent.CommandLine)) return expected;
        if (expected == "antigravity" && (name is "antigravity.exe" or "language_server_windows_x64.exe" ||
            name == "language_server.exe" && (parent.ExecutablePath ?? "").EndsWith(@"\Programs\Antigravity\resources\bin\language_server.exe", StringComparison.OrdinalIgnoreCase)))
            return expected;
        if (expected == "claude" && name == "claude.exe" && !ElectronChild().IsMatch(parent.CommandLine) &&
            (string.Equals(parent.ExecutablePath, Path.Combine(folders.Home, ".local", "bin", "claude.exe"), StringComparison.OrdinalIgnoreCase) ||
             DesktopCodeEngine().IsMatch(parent.ExecutablePath ?? "")))
            return expected;
        return null;
    }

    static LoadedEngines Windows(UserFolders folders, IReadOnlyList<ProcessInfo> processes)
    {
        var (versions, agents, unknown) = (new List<string>(), new List<LoadedConnection>(), false);
        var table = processes.GroupBy(p => p.ProcessId).ToDictionary(g => g.Key, g => g.First());
        var installed = JsonFiles.Read(Path.Combine(folders.Nexus, "nexus-windows", "installation.json"));
        var stable = Path.Combine(folders.Home, ".nexus", "nexus-windows", "NexusWindows.exe");
        foreach (var process in processes.Where(p => p.Name.Equals("NexusWindows.exe", StringComparison.OrdinalIgnoreCase) && AgentProfile().IsMatch(p.CommandLine)))
        {
            // Do not count an inert stable launcher alongside its child engine.
            if (string.Equals(process.ExecutablePath, stable, StringComparison.OrdinalIgnoreCase)) continue;
            if (Agent(process, table, folders) is not { } agent) continue;
            var receipt = Receipts(folders, agent, process).FirstOrDefault(r => ValidReceipt(r, agent, process));
            string? active = null;
            if (receipt.ValueKind == JsonValueKind.Object && receipt.Text("version") is { } version && Semantic().IsMatch(version))
            {
                versions.Add(version);
                // The recorded code file: NexusWindows.dll (framework-dependent) or NexusWindows.exe (Native AOT).
                if (installed is not { } record ||
                    !string.Equals(receipt.Text("executable"), record.Text("NativeExecutable"), StringComparison.OrdinalIgnoreCase) ||
                    record.Item("NativeArtifacts") is not { } artifacts || WindowsRuntime.CodeArtifact(artifacts) is not { } code ||
                    !string.Equals(receipt.Text("assemblySha256"), artifacts.Text(code), StringComparison.OrdinalIgnoreCase))
                    unknown = true;
                else active = version;
            }
            else unknown = true;
            // Keep every host-owned connection in its AI's verdict: a newer client does not hide an older,
            // unattested connection belonging to that same host.
            agents.Add(new LoadedConnection(agent, active, receipt.ValueKind == JsonValueKind.Object ? receipt.Text("processStartUtc") : null));
        }
        return new LoadedEngines(versions.Distinct().ToList(), unknown, agents);
    }

    static IEnumerable<JsonElement> Receipts(UserFolders folders, string agent, ProcessInfo process)
    {
        var folder = Path.Combine(folders.Nexus, "status", "runtime");
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, $"windows-{agent}-{process.ProcessId}-*.json"))
        {
            JsonElement? receipt;
            try { receipt = JsonFiles.Read(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { continue; }
            if (receipt is { } value) yield return value;
        }
    }

    static bool ValidReceipt(JsonElement receipt, string agent, ProcessInfo process)
    {
        if (receipt.ValueKind != JsonValueKind.Object || receipt.Item("schema")?.ToString() != "1" || receipt.Text("product") != "windows" ||
            receipt.Text("state") is not ("idle" or "busy") || receipt.Text("agent") != agent ||
            receipt.Item("pid")?.ToString() != process.ProcessId.ToString(CultureInfo.InvariantCulture) ||
            !string.Equals(receipt.Text("executable"), process.ExecutablePath, StringComparison.OrdinalIgnoreCase) || process.StartUtc is not { } started ||
            !DateTimeOffset.TryParse(receipt.Text("processStartUtc"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth) ||
            Math.Abs((birth.UtcDateTime - started).TotalSeconds) >= 0.01 ||
            receipt.Item("processStartTicks")?.ToString() != birth.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)) return false;
        var assembly = receipt.Text("assembly");
        // The engine's own rule (RuntimeReceipt): its assembly beside the executable, or the executable itself once
        // compiled ahead of time (Native AOT has no assembly file).
        var dll = Path.Combine(Path.GetDirectoryName(process.ExecutablePath!)!, "NexusWindows.dll");
        var expected = File.Exists(dll) ? dll : process.ExecutablePath!;
        return assembly is not null && assembly.Equals(expected, StringComparison.OrdinalIgnoreCase) &&
            receipt.Text("assemblySha256") is { } digest && Hex64().IsMatch(digest) && File.Exists(assembly) &&
            MaintenanceServices.Sha256(assembly).Equals(digest, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NexusBrowser's local endpoints (tests point them at stand-ins).</summary>
    public static string DaemonApi { get; set; } = "http://127.0.0.1:16182";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    static JsonElement? Get(string url, string? token = null, string? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(url));
            if (token is not null) request.Headers.Add("X-Nexus-Browser-Token", token);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = Http.Send(request);
            if (!response.IsSuccessStatusCode) return null;
            using var reader = new StreamReader(response.Content.ReadAsStream());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            return document.RootElement.Clone();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\s--http(?:\s|=)", RegexOptions.IgnoreCase)]
    private static partial Regex HttpMode();

    [GeneratedRegex(@"\s--(?:http|bridge)(?:\s|=)", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceMode();

    [GeneratedRegex(@"\s--bridge(?:\s|=|$)", RegexOptions.IgnoreCase)]
    private static partial Regex BridgeMode();

    static LoadedEngines Browser(UserFolders folders, IReadOnlyList<ProcessInfo> processes)
    {
        var (versions, unknown) = (new List<string>(), false);
        var installed = JsonFiles.Read(Path.Combine(folders.LocalAppData, "NexusBrowser", "current.json"));
        bool Runs(JsonElement? answer, Regex mode, bool fromInstalled) =>
            answer?.Item("pid")?.ToString() is { } pid && processes.Any(p => p.ProcessId.ToString(CultureInfo.InvariantCulture) == pid &&
                p.Name.Equals("NexusBrowser.exe", StringComparison.OrdinalIgnoreCase) && mode.IsMatch(p.CommandLine) &&
                (!fromInstalled || string.Equals(p.ExecutablePath, installed?.Text("exe"), StringComparison.OrdinalIgnoreCase)));
        var health = Get(DaemonApi + "/health");
        if (health?.Text("status") == "ok" && health.Value.Text("service") == "nexus-browser" &&
            health.Value.Text("version") is { } daemon && Semantic().IsMatch(daemon) && Runs(health, HttpMode(), false))
        {
            versions.Add(daemon);
            if (installed is null || !Runs(health, HttpMode(), true) ||
                health.Value.Text("fingerprint") is { Length: > 0 } print && !print.Equals(installed.Value.Text("fingerprint"), StringComparison.OrdinalIgnoreCase))
                unknown = true;
        }
        else if (processes.Any(p => p.Name.Equals("NexusBrowser.exe", StringComparison.OrdinalIgnoreCase) && ServiceMode().IsMatch(p.CommandLine))) unknown = true;
        var tokenFile = Path.Combine(folders.Nexus, "nexus-browser", "bridge-token");
        if (!File.Exists(tokenFile)) return new LoadedEngines(versions.Distinct().ToList(), true, []);
        var token = File.ReadAllText(tokenFile).Trim();
        var bridge = Get(BrowserBridge.Api + "/api/status", token);
        if (bridge?.Text("status") == "online" && bridge.Value.Text("version") is { } version && Semantic().IsMatch(version) && Runs(bridge, BridgeMode(), false))
        {
            versions.Add(version);
            if (installed is null || !string.Equals(bridge.Value.Text("fingerprint"), installed.Value.Text("fingerprint"), StringComparison.OrdinalIgnoreCase) ||
                !Runs(bridge, BridgeMode(), true)) unknown = true;
        }
        else unknown = true;
        // Ping is an authenticated read-only extension command with no AI/session identity. It neither takes tabs nor
        // changes focus. Its live response, not a package manifest, proves Chrome's version.
        var ping = Get(BrowserBridge.Api + "/api/command", token, """{"action":"ping","params":{},"timeout":3}""");
        if (ping?.Item("success")?.ValueKind == JsonValueKind.True && ping.Value.Item("data")?.Text("status") == "pong" &&
            ping.Value.Item("data")?.Text("version") is { } extension && Semantic().IsMatch(extension)) versions.Add(extension);
        else unknown = true;
        return new LoadedEngines(versions.Distinct().ToList(), unknown, []);
    }
}
