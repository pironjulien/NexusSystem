using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Nexus.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AppLog.Error("unhandled", e.Exception);
        // Crashes outside the UI thread and background tasks nobody awaited: written too, never silent.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("unhandled (background)", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Error("unobserved task", e.Exception);
        // One line per start: an empty log then means "no error", not "the log does not work".
        AppLog.Info($"start {typeof(App).Assembly.GetName().Version}");
        // --theme light|dark (pictures): the whole app, so that every brush follows; otherwise Windows' own theme.
        var argv = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(argv, "--theme");
        if (i >= 0 && i + 1 < argv.Length) RequestedTheme = argv[i + 1] == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }

    /// <summary>Command line: --lang fr|en, --theme light|dark, --capture &lt;folder&gt; (pictures of every page, then exit),
    /// --size WxH (the capture's window size, 1366x860 by default), --frames (video frames of the home page too),
    /// --quiet (shown away from the desktop without activation, for tests driven in the background).</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        string? Option(string name)
        {
            var i = Array.IndexOf(argv, name);
            return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
        }
        if (Option("--lang") is { } lang) Text.French = lang == "fr";
        var window = new MainWindow();
        if (Option("--capture") is { } folder)
        {
            var size = (Option("--size") ?? "1366x860").Split('x');
            _ = window.CaptureAsync(folder, new SizeInt32(int.Parse(size[0]), int.Parse(size[1])), argv.Contains("--frames"));
        }
        else if (argv.Contains("--quiet")) window.ShowQuietly();  // tests: never takes the user's foreground
        else window.Activate();
    }
}
