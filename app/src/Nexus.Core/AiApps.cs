using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Nexus.Core;

public enum AiApp
{
    ClaudeCode,
    Codex,
    Antigravity,
}

/// <summary>
/// Which AI apps are on this PC: the app's pages, the hourly maintenance and the pack (NexusMaintenance.exe --install,
/// started by get-nexus.ps1) use this one detection.
/// </summary>
public static partial class AiApps
{
    public static readonly IReadOnlyList<AiApp> All = [AiApp.ClaudeCode, AiApp.Codex, AiApp.Antigravity];

    /// <summary>get-nexus.ps1's names of the AI apps, in its display order (Claude Desktop is a configuration of its own).</summary>
    public static readonly IReadOnlyList<string> Keys = ["claude-code", "claude-desktop", "codex", "antigravity"];

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
        var keys = FindKeys(folders);
        return All.Where(app => keys.Contains(Key(app))).ToList();
    }

    /// <summary>The AI apps found, by get-nexus.ps1 name (claude-desktop included), in <see cref="Keys"/> order.</summary>
    /// <param name="onPath">How a command is found on PATH (tests: a PATH of their own); <see cref="OnPath"/> by default.</param>
    public static IReadOnlyList<string> FindKeys(UserFolders folders, Func<string, string?>? onPath = null)
    {
        onPath ??= OnPath;
        var found = new HashSet<string>();
        var desktop = DesktopConfigFolders(folders).Count > 0;
        if (desktop) found.Add("claude-desktop");
        if (desktop || Directory.Exists(Path.Combine(folders.Home, ".claude")) || FindClaudeCli(folders, onPath) is not null)
            found.Add("claude-code");  // Claude Desktop's Code tab is Claude Code
        if (Directory.Exists(Path.Combine(folders.Home, ".codex")) || onPath("codex") is not null)
            found.Add("codex");
        var gemini = Path.Combine(folders.Home, ".gemini");
        if (Directory.Exists(Path.Combine(gemini, "antigravity")) || Directory.Exists(Path.Combine(gemini, "config")) ||
            Directory.Exists(Path.Combine(folders.LocalAppData, "Programs", "Antigravity")))
            found.Add("antigravity");
        return Keys.Where(found.Contains).ToList();
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

    [GeneratedRegex("^AnthropicPBC\\.Claude_[a-z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ClaudePackage();

    [GeneratedRegex("^[0-9a-f]{12,64}$")]
    private static partial Regex BuildFolder();

    [GeneratedRegex(@"^(\d+\.\d+\.\d+)(?:\.0)?$")]
    private static partial Regex EngineVersion();

    /// <summary>
    /// Claude Code's command: on PATH, the native installer's ~\.local\bin\claude.exe, else the Code engine Claude
    /// Desktop installed (its known data layouts only, never a recursive search), signed by Anthropic and naming itself
    /// Claude Code in its version resource. Discovery executes nothing.
    /// </summary>
    public static string? FindClaudeCli(UserFolders folders, Func<string, string?>? onPath = null)
    {
        if ((onPath ?? OnPath)("claude") is { } command) return command;
        var local = Path.Combine(folders.Home, ".local", "bin", "claude.exe");
        if (File.Exists(local)) return local;
        var roots = new List<string> { Path.Combine(folders.RoamingAppData, "Claude") };
        foreach (var (layoutRoot, suffix) in new[] { (Path.Combine(folders.LocalAppData, "Claude-Data"), Array.Empty<string>()),
            (Path.Combine(folders.LocalAppData, "Packages"), new[] { "LocalCache", "Roaming", "Claude" }) })
        {
            if (!Directory.Exists(layoutRoot) || IsLink(layoutRoot)) continue;
            foreach (var package in Directory.EnumerateDirectories(layoutRoot))
            {
                if (!ClaudePackage().IsMatch(Path.GetFileName(package)) || IsLink(package)) continue;
                var folder = package;
                var safe = true;
                foreach (var part in suffix)
                {
                    folder = Path.Combine(folder, part);
                    if (!Directory.Exists(folder) || IsLink(folder)) { safe = false; break; }
                }
                if (safe) roots.Add(folder);
            }
        }
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root) || IsLink(root) || Directory.EnumerateFiles(root, ".user-data-left.*").Any()) continue;
            var code = Path.Combine(root, "claude-code");
            if (!Directory.Exists(code) || IsLink(code)) continue;
            var versions = Directory.EnumerateDirectories(code)
                .Where(v => Version.TryParse(Path.GetFileName(v), out var parsed) && parsed.Revision < 0 && parsed.Build >= 0 && !IsLink(v))
                .OrderByDescending(v => Version.Parse(Path.GetFileName(v)));
            foreach (var version in versions)
            {
                var builds = Directory.EnumerateDirectories(version).Where(b => BuildFolder().IsMatch(Path.GetFileName(b)) && !IsLink(b))
                    .OrderByDescending(Directory.GetLastWriteTimeUtc);
                foreach (var build in builds)
                {
                    var candidate = Path.Combine(build, "claude.exe");
                    if (!File.Exists(candidate) || IsLink(candidate) || !Authenticode.IsSignedBy(candidate, "Anthropic, PBC")) continue;
                    // Read the signed file's identity only after authenticating it. Another Anthropic application is
                    // not a Code engine, even if somebody placed it in a Desktop engine directory.
                    var identity = FileVersionInfo.GetVersionInfo(candidate);
                    if (identity.ProductName != "Claude Code" || identity.CompanyName != "Anthropic PBC" ||
                        EngineVersion().Match(identity.ProductVersion ?? "") is not { Success: true } engine ||
                        engine.Groups[1].Value != Path.GetFileName(version)) continue;
                    return candidate;
                }
            }
        }
        return null;
    }

    static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    public static string? OnPath(string command)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try { candidate = Path.Combine(folder.Trim(), command + extension.ToLowerInvariant()); }
                catch (ArgumentException) { break; }
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
