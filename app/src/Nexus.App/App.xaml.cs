using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.UI.Xaml;
using Nexus.Core;
using Windows.Graphics;

namespace Nexus.App;

// WinUI owns the application's lifetime and never disposes it: the instance gate is disposed when the main window closes.
[SuppressMessage("Design", "CA1001", Justification = "Disposed on the main window's Closed event; WinUI never disposes an Application.")]
public partial class App : Application
{
    AppInstanceGate? instanceGate;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AppLog.Error("unhandled", e.Exception);
        // Crashes outside the UI thread and background tasks nobody awaited: written too, never silent.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("unhandled (background)", e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Error("unobserved task", e.Exception);
        // One line per start: an empty log then means "no error", not "the log does not work".
        AppLog.Info($"start {typeof(App).Assembly.GetName().Version}");
        // --theme light|dark (pictures): the whole app, so that every brush follows; otherwise Windows' own theme.
        var argv = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(argv, "--theme");
        if (i >= 0 && i + 1 < argv.Length) RequestedTheme = argv[i + 1] == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }

    /// <summary>Command line: --lang fr|en, --theme light|dark, --capture &lt;folder&gt; (pictures of every page, then exit),
    /// --size WxH (the capture's window size, 1366x860 by default),
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
        if (Option("--capture") is null && !argv.Contains("--quiet"))
        {
            instanceGate = new AppInstanceGate();
            if (!instanceGate.IsFirst)
            {
                instanceGate.RequestActivation();
                instanceGate.Dispose();
                instanceGate = null;
                Exit();
                return;
            }
        }
        var window = new MainWindow();
        if (instanceGate is { } gate)
        {
            var dispatcher = window.DispatcherQueue;
            gate.Listen(() => dispatcher.TryEnqueue(() =>
            {
                if (instanceGate is null) return;
                if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter &&
                    presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) presenter.Restore();
                window.Activate();
            }));
            window.Closed += (_, _) => { var closing = instanceGate; instanceGate = null; closing?.Dispose(); };
        }
        if (Option("--capture") is { } folder)
        {
            var size = (Option("--size") ?? "1366x860").Split('x');
            _ = window.CaptureAsync(folder, new SizeInt32(int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture)));
        }
        else if (argv.Contains("--quiet")) window.ShowQuietly();  // tests: never takes the user's foreground
        else window.Activate();
        // An existing Store installation may predate the maintenance task: the native engine of this build's catalog
        // release (checked) and its task are installed in the background, without PowerShell; normal use stays
        // responsive and capture fixtures never schedule real maintenance.
        if (Option("--capture") is null && !argv.Contains("--quiet")) _ = BootstrapMaintenanceAsync();
    }

    static async Task BootstrapMaintenanceAsync()
    {
        try
        {
            await Task.Run(() => new MaintenanceSetup(UserFolders.FromEnvironment()).BootstrapAsync());
        }
        catch (Exception error) { AppLog.Error("maintenance bootstrap", error); }
    }
}
