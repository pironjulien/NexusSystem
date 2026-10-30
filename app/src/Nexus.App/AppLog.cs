namespace Nexus.App;

/// <summary>The app's own error log, %LOCALAPPDATA%\NexusSystem\app.log (Nexus.Core.ErrorLog: timestamped, capped, never
/// screen text). Installed from its MSIX package, Windows redirects that folder to
/// %LOCALAPPDATA%\Packages\&lt;package&gt;\LocalCache\Local\NexusSystem\app.log.</summary>
public static class AppLog
{
    static readonly Nexus.Core.ErrorLog Log = new(
        Path.Combine(Nexus.Core.UserFolders.FromEnvironment().LocalAppData, "NexusSystem", "app.log"));

    public static string File => Log.File;

    public static void Error(string where, Exception error) => Log.Error(where, error);

    public static void Info(string text) => Log.Info(text);
}
