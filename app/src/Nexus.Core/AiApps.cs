namespace Nexus.Core;

public enum AiApp
{
    ClaudeCode,
    Codex,
    Antigravity,
}

/// <summary>
/// Which AI apps are on this PC, found the same way as get-nexus.ps1 (Find-AiApp), so that the app and the one-line
/// install always agree.
/// </summary>
public static class AiApps
{
    public static readonly IReadOnlyList<AiApp> All = [AiApp.ClaudeCode, AiApp.Codex, AiApp.Antigravity];

    /// <summary>The name get-nexus.ps1 takes in -Only and -Skip.</summary>
    public static string Key(AiApp app) => app switch
    {
        AiApp.ClaudeCode => "claude-code",
        AiApp.Codex => "codex",
        _ => "antigravity",
    };

    public static string Name(AiApp app) => app switch
    {
        AiApp.ClaudeCode => "Claude Code",
        AiApp.Codex => "Codex",
        _ => "Antigravity",
    };

    public static IReadOnlyList<AiApp> Find(UserFolders folders)
    {
        var found = new List<AiApp>();
        var desktop = DesktopConfigFolders(folders).Count > 0;
        if (desktop || FindClaudeCli(folders) is not null || Directory.Exists(Path.Combine(folders.Home, ".claude")))
            found.Add(AiApp.ClaudeCode);  // Claude Desktop's Code tab is Claude Code
        if (Directory.Exists(Path.Combine(folders.Home, ".codex")) || OnPath("codex") is not null)
            found.Add(AiApp.Codex);
        var gemini = Path.Combine(folders.Home, ".gemini");
        if (Directory.Exists(Path.Combine(gemini, "antigravity")) || Directory.Exists(Path.Combine(gemini, "config")) ||
            Directory.Exists(Path.Combine(folders.LocalAppData, "Programs", "Antigravity")))
            found.Add(AiApp.Antigravity);
        return found;
    }

    /// <summary>
    /// Where Claude Desktop reads claude_desktop_config.json: %APPDATA%\Claude, the Store package's redirected
    /// folder, and the newer Store versions' %LOCALAPPDATA%\Claude-Data\&lt;package&gt;. A folder whose data moved away
    /// keeps only a ".user-data-left.*" marker: Claude no longer reads it.
    /// </summary>
    public static IReadOnlyList<string> DesktopConfigFolders(UserFolders folders)
    {
        var candidates = new List<string> { Path.Combine(folders.RoamingAppData, "Claude") };
        var packages = Path.Combine(folders.LocalAppData, "Packages");
        if (Directory.Exists(packages))
            candidates.AddRange(Directory.EnumerateDirectories(packages, "*Claude_*").Select(p => Path.Combine(p, "LocalCache", "Roaming", "Claude")));
        var data = Path.Combine(folders.LocalAppData, "Claude-Data");
        if (Directory.Exists(data))
            candidates.AddRange(Directory.EnumerateDirectories(data));
        return candidates.Where(c => Directory.Exists(c) && !Directory.EnumerateFiles(c, ".user-data-left.*").Any()).ToList();
    }

    public static string? FindClaudeCli(UserFolders folders)
    {
        var local = Path.Combine(folders.Home, ".local", "bin", "claude.exe");
        return File.Exists(local) ? local : OnPath("claude");
    }

    public static string? OnPath(string command)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder.Trim(), command + extension.ToLowerInvariant());
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
