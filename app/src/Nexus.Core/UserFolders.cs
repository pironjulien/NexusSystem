namespace Nexus.Core;

/// <summary>
/// The user's folders, read from the environment like the product installers do (USERPROFILE, APPDATA,
/// LOCALAPPDATA), so that tests can point them at a throwaway profile.
/// </summary>
public sealed record UserFolders(string Home, string RoamingAppData, string LocalAppData)
{
    public static UserFolders FromEnvironment() => new(
        Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string ClaudeSettings => Path.Combine(Home, ".claude", "settings.json");
    public string CodexConfig => Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Home, ".codex"), "config.toml");
    public string AntigravityPlugins => Path.Combine(Home, ".gemini", "config", "plugins");
    public string Nexus => Path.Combine(Home, ".nexus");
    // ~\.nexus\journal: AppData is virtualized for MSIX apps (this one and Claude Desktop), so the products write there.
    public string Journal => Environment.GetEnvironmentVariable("NEXUS_JOURNAL_HOME") ?? Path.Combine(Nexus, "journal");
}
