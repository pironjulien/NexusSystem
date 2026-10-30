using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>
/// The verified shared core: its immutable versioned executable, and the stable path its installer keeps beside it
/// (~\.nexus\nexus-windows\NexusWindows.exe), which the catalog plugins start. Both are the same common core.
/// </summary>
public sealed record WindowsInstallation(string Version, string ExecutablePath, string NativeHash, string? StablePath = null)
{
    public bool IsCore(string? executable) => executable is not null &&
        (string.Equals(executable, ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
         StablePath is not null && string.Equals(executable, StablePath, StringComparison.OrdinalIgnoreCase));
}

public enum WindowsConnection { Idle, Common, Previous, Unconfigured }

/// <summary>The shared native core and its separate MCP client processes. Installation is not service health.</summary>
public static class WindowsRuntime
{
    /// <summary>The stable path NexusWindows' installer keeps for every client: what the catalog plugins start.</summary>
    public static string StablePath(UserFolders folders) => Path.Combine(folders.Nexus, "nexus-windows", "NexusWindows.exe");

    public static WindowsInstallation? ReadInstallation(UserFolders folders)
    {
        try
        {
            var root = Path.Combine(folders.Nexus, "nexus-windows");
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "installation.json")));
            var data = document.RootElement;
            if (data.GetProperty("Schema").GetInt32() != 2) return null;
            var hash = data.GetProperty("NativeHash").GetString();
            var version = data.GetProperty("Version").GetString();
            var executable = data.GetProperty("NativeExecutable").GetString();
            if (hash is null || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$") || !System.Version.TryParse(version, out _)) return null;
            var expected = Path.Combine(root, "native", hash, "NexusWindows.exe");
            if (executable is null || !Path.IsPathFullyQualified(executable) ||
                !Path.GetFullPath(executable).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return null;
            // The immutable folder name is a source fingerprint, not the executable's SHA256.
            // Validate the installed artifacts against their own manifest, including managed code and runtime config.
            var artifacts = data.GetProperty("NativeArtifacts");
            if (artifacts.ValueKind != JsonValueKind.Object) return null;
            foreach (var required in new[] { "NexusWindows.exe", "NexusWindows.dll", "NexusWindows.deps.json", "NexusWindows.runtimeconfig.json" })
                if (!artifacts.TryGetProperty(required, out _)) return null;
            foreach (var artifact in artifacts.EnumerateObject())
            {
                if (artifact.Name.IndexOfAny(['/', '\\', ':']) >= 0 || artifact.Name is "." or "..") return null;
                var digest = artifact.Value.GetString();
                if (digest is null || !Regex.IsMatch(digest, "^[a-fA-F0-9]{64}$")) return null;
                using var file = File.OpenRead(Path.Combine(Path.GetDirectoryName(expected)!, artifact.Name));
                if (!Convert.ToHexString(SHA256.HashData(file)).Equals(digest, StringComparison.OrdinalIgnoreCase)) return null;
            }
            // The stable path counts only once its installer has put it there.
            var stable = StablePath(folders);
            return new WindowsInstallation(version!, expected, hash, File.Exists(stable) ? stable : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
        {
            return null; // A manifest being replaced, missing files or mismatched bytes are never a verified installation.
        }
    }

    public static WindowsConnection Connection(AiApp app, IEnumerable<RunningServer> running, WindowsInstallation? installation)
    {
        return ClientConnection(running.Where(server => server.Product == Products.Windows && server.StartedBy == app && !server.ClaudeDesktopClient), installation);
    }

    public static WindowsConnection DesktopConnection(DeclarationState declaration, IEnumerable<RunningServer> running, WindowsInstallation? installation)
    {
        var state = ClientConnection(running.Where(server => server.Product == Products.Windows && server.ClaudeDesktopClient), installation);
        return state == WindowsConnection.Common && declaration != DeclarationState.Declared ? WindowsConnection.Unconfigured : state;
    }

    static WindowsConnection ClientConnection(IEnumerable<RunningServer> running, WindowsInstallation? installation)
    {
        var clients = running.ToList();
        // Any older loaded connection matters even if a new client is also running for the same AI.
        if (clients.Any(server => !server.CommonWindowsCore || installation is null || !installation.IsCore(server.ExecutablePath))) return WindowsConnection.Previous;
        return clients.Count > 0 ? WindowsConnection.Common : WindowsConnection.Idle;
    }

    /// <summary>Desktop has its own configuration; a Claude Code plugin never proves a Desktop connection.</summary>
    public static DeclarationState DesktopDeclaration(UserFolders folders, WindowsInstallation? installation)
    {
        var states = new HashSet<DeclarationState>();
        foreach (var folder in AiApps.DesktopConfigFolders(folders))
        {
            var file = Path.Combine(folder, "claude_desktop_config.json");
            if (!File.Exists(file)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                if (!document.RootElement.TryGetProperty("mcpServers", out var servers)) continue;
                foreach (var name in new[] { "nexus-windows", "nexus-windows-claude" })
                {
                    if (!servers.TryGetProperty(name, out var server)) continue;
                    if (server.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
                    {
                        states.Add(DeclarationState.Disabled);
                        continue;
                    }
                    var common = installation is not null && server.TryGetProperty("command", out var command) &&
                        installation.IsCore(command.GetString()) &&
                        server.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array &&
                        args.EnumerateArray().Select(arg => arg.GetString()).SequenceEqual(["--mcp", "--agent", "claude"], StringComparer.OrdinalIgnoreCase);
                    states.Add(common ? DeclarationState.Declared : DeclarationState.Invalid);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                states.Add(DeclarationState.Invalid);
            }
        }
        if (DesktopExtension.For(Products.Windows)?.IsInstalled(folders) == true) states.Add(DeclarationState.Invalid);
        return states.Count == 0 ? DeclarationState.Missing : states.Count == 1 ? states.Single() : DeclarationState.Invalid;
    }

    /// <summary>Only the exact shared engine MCP invocation counts; helpers, test runs and tool CLI calls do not.</summary>
    public static bool TryMcpCommand(string imageName, string commandLine, out string? executable, out AiApp? agent)
    {
        executable = null;
        agent = null;
        return imageName.Equals("NexusWindows.exe", StringComparison.OrdinalIgnoreCase) &&
            TryNativeClientCommand(imageName, commandLine, out executable, out agent);
    }

    internal static bool TryNativeClientCommand(string imageName, string commandLine, out string? executable, out AiApp? agent)
    {
        executable = null;
        agent = null;
        // The three profile names have no spaces. Reject unknown arguments, escaped quotes and mixed execution modes.
        var match = Regex.Match(commandLine,
            "^\\s*(?:\"(?<exe>[^\"]+)\"|(?<exe>[^\\s\"]+))\\s+--mcp\\s+--agent\\s+(?<agent>claude|codex|antigravity)\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var path = match.Groups["exe"].Value;
        if (!Path.GetFileName(path).Equals(imageName, StringComparison.OrdinalIgnoreCase)) return false;
        executable = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
        agent = match.Groups["agent"].Value.ToLowerInvariant() switch
        {
            "claude" => AiApp.ClaudeCode,
            "codex" => AiApp.Codex,
            _ => AiApp.Antigravity,
        };
        return true;
    }
}
