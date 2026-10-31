using System.Management;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>A running MCP server of a product, and the AI app that started it.</summary>
public sealed record RunningServer(Product Product, int ProcessId, AiApp? StartedBy,
    string? ExecutablePath = null, bool CommonWindowsCore = false, bool ClaudeDesktopClient = false);

public sealed record ServerProcess(int Parent, string Name, string CommandLine, string? ExecutablePath = null);

/// <summary>
/// Live state: which Nexus servers run right now and for which AI app, read from the process list (command line
/// and parent chain), without talking to the servers.
/// </summary>
public static class Servers
{
    // What the command line of a former (npm or PyPI) installation's server contains, per product, recognised so the app shows it as a previous connection.
    static readonly IReadOnlyDictionary<string, string[]> Packages = new Dictionary<string, string[]>
    {
        ["windows"] = ["nexus-windows-claude", "nexus-windows-codex", "nexus-windows-antigravity", "nexus_windows"],
        ["android"] = ["nexus-android", "nexus_android"],
        ["browser"] = ["nexus-browser", "nexus_browser", "NexusBrowser\\app\\client\\mcp_server.py"],
        ["relay"] = ["nexus-relay", "nexus_relay"],
        ["agymail"] = ["nexus-agymail", "nexus_agymail"],
        ["indesign"] = ["indesign-mcp-secure"],
    };

    public static IReadOnlyList<RunningServer> Find()
    {
        var processes = new Dictionary<int, ServerProcess>();
        using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process"))
        {
            foreach (var process in searcher.Get().Cast<ManagementObject>())
            {
                using (process)
                {
                    processes[(int)(uint)process["ProcessId"]] = new ServerProcess(
                        (int)(uint)process["ParentProcessId"],
                        (string?)process["Name"] ?? "",
                        (string?)process["CommandLine"] ?? "", (string?)process["ExecutablePath"]);
                }
            }
        }
        return Match(processes);
    }

    /// <summary>Servers among a process table (pid → parent, image name, command line); one per server tree.</summary>
    public static IReadOnlyList<RunningServer> Match(IReadOnlyDictionary<int, (int Parent, string Name, string CommandLine)> processes) =>
        Match(processes.ToDictionary(entry => entry.Key, entry => new ServerProcess(entry.Value.Parent, entry.Value.Name, entry.Value.CommandLine)));

    public static IReadOnlyList<RunningServer> Match(IReadOnlyDictionary<int, ServerProcess> processes)
    {
        var found = new List<RunningServer>();
        foreach (var (pid, process) in processes)
        {
            var product = ProductOf(process.Name, process.CommandLine);
            if (product is null) continue;
            var common = WindowsRuntime.TryMcpCommand(process.Name, process.CommandLine, out var executable, out var agent);
            if (product == Products.Windows && process.ExecutablePath is { Length: > 0 } actualExecutable)
                executable = actualExecutable; // WMI's loaded image wins over argv[0] when available.
            // Prefer the identifiable shared core over a legacy wrapper that merely mentions its package.
            if (!common && product == Products.Windows && processes.Values.Any(child => child.Parent == pid &&
                WindowsRuntime.TryMcpCommand(child.Name, child.CommandLine, out _, out _))) continue;
            // A launcher of a former installation (npm, PyPI) starts its runtime: keep only the top of each server tree.
            if (processes.TryGetValue(process.Parent, out var parent) && ProductOf(parent.Name, parent.CommandLine) == product &&
                (!common || WindowsRuntime.TryMcpCommand(parent.Name, parent.CommandLine, out _, out _))) continue;
            // Shared-core declarations start the exact executable directly. A Python/PowerShell qualification
            // client somewhere below an AI host is not that host's MCP connection.
            // Codex reads no variable in a plugin's command: the catalog's Codex plugin starts the engine from its
            // stable folder through cmd.exe, which is then the one step between Codex and the engine.
            var from = process.Parent;
            if (common && processes.TryGetValue(from, out var launcher) && IsCodexLauncher(launcher)) from = launcher.Parent;
            var (owner, desktop) = StartedBy(processes, from, common ? 1 : 8);
            // --agent is also used by isolated qualification clients. It is not proof that the actual AI host loaded it.
            if (common && (desktop ? agent != AiApp.ClaudeCode : owner != agent)) { owner = null; desktop = false; }
            found.Add(new RunningServer(product, pid, owner, executable, common, desktop));
        }
        return found;
    }

    static Product? ProductOf(string name, string commandLine)
    {
        if (name.Equals("NexusWindows.exe", StringComparison.OrdinalIgnoreCase))
            return WindowsRuntime.TryMcpCommand(name, commandLine, out _, out _) ? Products.Windows : null;
        if (name.Equals("NexusWindows.Antigravity.exe", StringComparison.OrdinalIgnoreCase))
            return WindowsRuntime.TryNativeClientCommand(name, commandLine, out _, out var legacyAgent) && legacyAgent == AiApp.Antigravity
                ? Products.Windows : null;
        // Match actual native server modes, not a build/selftest, a UIA helper or Chrome's one-shot auth host.
        var native = name.ToLowerInvariant() switch
        {
            "nexusandroid.exe" => Products.Android,
            "nexusbrowser.exe" => Products.Browser,
            "nexusrelay.exe" => Products.Relay,
            "nexusagymail.exe" => Products.AgyMail,
            "nexusindesign.exe" => Products.InDesign,
            "nexuscodex.uia.exe" => Products.Windows,
            _ => null,
        };
        if (native is not null)
            return Regex.IsMatch(commandLine, @"(?:^|\s)--(?:mcp|http|bridge)(?=\s|=|$)", RegexOptions.IgnoreCase)
                ? native : null;
        foreach (var (key, needles) in Packages)
        {
            if (needles.Any(n => commandLine.Contains(n, StringComparison.OrdinalIgnoreCase))) return Products.ByKey(key);
        }
        return null;
    }

    /// <summary>The nexus-windows Codex plugin's launcher: cmd.exe /d /c cd /d ...\.nexus\nexus-windows &amp;&amp; .\NexusWindows.exe --mcp --agent codex.</summary>
    public static bool IsCodexLauncher(ServerProcess process) =>
        process.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(process.CommandLine, @"\s/d\s+/c\s+cd\s+/d\s+\S+\\\.nexus\\nexus-windows\s+&&\s+\.\\NexusWindows\.exe\s+--mcp\s+--agent\s+codex\s*$", RegexOptions.IgnoreCase);

    static (AiApp? App, bool Desktop) StartedBy(IReadOnlyDictionary<int, ServerProcess> processes, int pid, int maximumDepth)
    {
        for (var depth = 0; depth < maximumDepth && processes.TryGetValue(pid, out var process); depth++, pid = process.Parent)
        {
            var name = process.Name.ToLowerInvariant();
            if (name is "codex.exe")
                return Regex.IsMatch(process.CommandLine, @"(?:^|\s)exec(?=\s|$)", RegexOptions.IgnoreCase)
                    ? (null, false) : (AiApp.Codex, false);
            if (name is "antigravity.exe" or "language_server_windows_x64.exe") return (AiApp.Antigravity, false);
            var path = process.ExecutablePath ?? CommandExecutable(process.CommandLine);
            if (name is "language_server.exe" && path.EndsWith(@"\Programs\Antigravity\resources\bin\language_server.exe", StringComparison.OrdinalIgnoreCase))
                return (AiApp.Antigravity, false);
            if (name is "claude.exe")
            {
                // Observed Store desktop image, including its Electron --type children. They never attest Code.
                if (Regex.IsMatch(path, @"\\WindowsApps\\AnthropicPBC\.Claude_[^\\]+\\app\\Claude\.exe$", RegexOptions.IgnoreCase))
                    return (null, true);
                // The native Claude CLI location is also used by AiApps.FindClaudeCli. An unknown claude.exe
                // or a generic Electron child cannot be identified from its name or --agent profile alone.
                if (path.EndsWith(@"\.local\bin\claude.exe", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(process.CommandLine, @"(?:^|\s)--type(?:=|\s)", RegexOptions.IgnoreCase)) return (AiApp.ClaudeCode, false);
                return (null, false);
            }
        }
        return (null, false);
    }

    static string CommandExecutable(string commandLine) =>
        Regex.Match(commandLine, "^\\s*(?:\"(?<exe>[^\"]+)\"|(?<exe>[^\\s\"]+))").Groups["exe"].Value;
}
