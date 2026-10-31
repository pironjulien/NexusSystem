using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// A product's control panel: status per AI app (declared in its configuration, server running and who started it),
/// what the AI apps did (the common journal, 24 h), safeguards and the emergency stop, update and the product page.
/// </summary>
public sealed partial class ProductPage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly TextBlock stopState = new() { Visibility = Visibility.Collapsed };
    readonly CancellationTokenSource lifetime = new();
    bool refreshingStatus;
    DispatcherTimer? timer;

    public ProductPage(Product product)
    {
        Unloaded += (_, _) => { timer?.Stop(); lifetime.Cancel(); };
        var about = Text.Get(char.ToUpperInvariant(product.Key[0]) + product.Key[1..] + ".About");
        var header = Ui.Header(Ui.Logo(product.Key, 56), product.Name, about);
        var page = Ui.Column(20, header);
        var link = new HyperlinkButton { Content = Text.Get("OpenPage"), NavigateUri = new Uri(product.Page), Padding = new Thickness(0) };

        if (product.Availability != Availability.Ready)
        {
            var text = product.Availability switch
            {
                Availability.Later => Text.Get("Later"),
                Availability.OtherStore => Text.Get("OtherStore"),
                _ => Text.Get("NotPublic"),
            };
            page.Children.Add(Ui.Card(Ui.Column(8, Ui.Body(text), link), product.Accent));
            Content = Ui.Page(page);
            return;
        }

        // Status: per AI app it serves here (declared, server running), or the device's plugin.
        var ais = AiApps.Find(folders);
        var status = Ui.Column(10, Ui.Strong(Text.Get("Status")));
        _ = RefreshStatusAsync(product, status);
        page.Children.Add(Ui.Card(status, product.Accent));

        foreach (var step in Steps.For(product, folders, ais)) page.Children.Add(step);

        // Published, installed and observed versions remain separate; maintenance never interrupts an AI session.
        var versions = Ui.Column(4);
        var updateNote = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords, Visibility = Visibility.Collapsed };
        var version = Ui.Column(10, Ui.Strong(Text.Get("Version.Title")), versions);
        if (ProductUpdates.CanRequest(product, folders))
        {
            var update = new Button { Content = Text.Get("Version.Button") };
            update.Click += async (_, _) =>
            {
                update.IsEnabled = false;
                var answer = await ProductUpdates.RequestManagedAsync(product, folders, Http, lifetime.Token);
                updateNote.Text = Text.Get("Update." + answer);
                updateNote.Visibility = Visibility.Visible;
                update.IsEnabled = true;
                RefreshVersions(product, versions);
            };
            version.Children.Add(update);
        }
        else if (product.Device is null)
        {
            version.Children.Add(Ui.Body(Text.Get("Version.Auto"), secondary: true));
        }
        if (MaintenanceUpdates.Supports(product) && MaintenanceUpdates.IsInstalled(folders))
            version.Children.Add(Ui.Body(Text.Get("Version.Managed"), secondary: true));
        version.Children.Add(updateNote);
        RefreshVersions(product, versions);
        _ = RefreshReleaseAsync(product, versions);
        page.Children.Add(Ui.Card(version));

        // What the AI apps did (products that write the common journal).
        var entries = Journal.Read(product, folders, DateTimeOffset.UtcNow.AddHours(-24), limit: 200);
        var activity = Ui.Column(10, Ui.Strong(Text.Get("Activity")));
        if (entries.Count == 0) activity.Children.Add(Ui.Body(Text.Get("NoActivity"), secondary: true));
        else
        {
            var (count, verified, failed, _) = Journal.Summarize(entries);
            activity.Children.Add(Ui.Body(Text.Format("ActivitySummary", count, verified)));
            if (failed > 0) activity.Children.Add(Ui.Body(Text.Format("ActivityFailed", failed), secondary: true));
            // The raw journal stays one click away, for whoever wants it.
            activity.Children.Add(new Expander
            {
                Header = Text.Get("ActivityDetails"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Lines(entries.Take(60).Select(entry =>
                    $"{entry.At.ToLocalTime():HH:mm:ss}  {entry.Outcome ?? "·"}  {entry.Agent,-11} {entry.Action,-12} {entry.App}{(entry.Error is null ? "" : "  — " + entry.Error)}"), 300),
            });
        }
        if (product.JournalFolder is not null) page.Children.Add(Ui.Card(activity));

        // Safeguards and the emergency stop.
        var stop = new Button { Content = Text.Get("Stop"), Background = Ui.Theme("SystemFillColorCriticalBackgroundBrush") };
        stop.Click += (_, _) => { EmergencyStop.Press(folders); RefreshStop(); };
        page.Children.Add(Ui.Card(Ui.Column(10,
            Ui.Strong(Text.Get("Safety")),
            Ui.Body(Text.Get("SafetyText"), secondary: true),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { stop, Ui.Body(Text.Get("StopText"), secondary: true) } },
            stopState)));

        page.Children.Add(link);
        Content = Ui.Page(page);

        RefreshStop();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var ticks = 0;
        timer.Tick += async (_, _) =>
        {
            RefreshStop();
            if (++ticks % 5 == 0)
            {
                RefreshVersions(product, versions);
                await RefreshStatusAsync(product, status);
            }
        };
        timer.Start();
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    async Task RefreshStatusAsync(Product product, StackPanel status)
    {
        if (refreshingStatus || lifetime.IsCancellationRequested) return;
        refreshingStatus = true;
        try
        {
            // WMI may take time: never block the UI, overlap refreshes or update an unloaded page.
            var (running, windows) = await Task.Run(() =>
            {
                var processes = Servers.Find().Where(s => s.Product == product).ToList();
                var installation = product == Products.Windows ? WindowsRuntime.ReadInstallation(folders) : null;
                return (processes, installation);
            }, lifetime.Token);
            var bridge = product == Products.Browser
                ? await BrowserBridge.StateAsync(folders, Http, lifetime.Token) : (BridgeState?)null;
            if (lifetime.IsCancellationRequested) return;
            var ais = AiApps.Find(folders);
            status.Children.Clear();
            status.Children.Add(Ui.Strong(Text.Get("Status")));
            if (product.Device is not null)
            {
                if (!Products.LogiService(folders)) status.Children.Add(Ui.Body(Text.Get("NeedsLogi"), secondary: true));
                else
                {
                    var installed = Declarations.DeviceInstalled(product, folders);
                    status.Children.Add(Ui.Mark(installed, $"{product.Device} · {Text.Get(installed ? "PluginInstalled" : "PluginMissing")}"));
                }
            }
            else if (Products.Why(product, ais, folders) is { } unfit)
                status.Children.Add(Ui.Body(unfit == Unfit.NeedsApp ? Text.Get("NeedsClaudeCode") : unfit == Unfit.NeedsTwo ? Text.Get("NeedsTwo")
                    : Text.Format("OnlyFor", string.Join(", ", product.Targets.Select(AiApps.Name))), secondary: true));
            else
            {
                foreach (var app in ais.Where(product.Targets.Contains))
                {
                    var declared = Declarations.State(product, app, folders);
                    // A declared Windows connection without the shared engine installed cannot work: it needs repair.
                    if (product == Products.Windows && windows is null && declared == DeclarationState.Declared) declared = DeclarationState.Invalid;
                    // "In use" only for a server this AI started: a shared daemon runs whether or not the AI is open.
                    var live = running.Any(s => s.StartedBy == app);
                    // One line per AI: ready or not, then only what the user can act on (in use, restart, repair).
                    var presence = product == Products.Windows
                        ? Text.Format("Windows.Connection." + WindowsRuntime.Connection(app, running, windows), AiApps.Name(app))
                        : Text.Get(live ? "Running" : "NotRunning");
                    var label = $"{AiApps.Name(app)} · {Text.Get("Declaration." + declared)}";
                    var row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
                        // Turned off on purpose: neither a success nor a fault, so no mark.
                        Children = { Ui.AiLogo(app, 18), declared == DeclarationState.Disabled ? Ui.Body(label, secondary: true)
                            : Ui.Mark(declared == DeclarationState.Declared, label) },
                    };
                    if (declared == DeclarationState.Declared && presence.Length > 0) row.Children.Add(Ui.Body(presence, secondary: true));
                    status.Children.Add(row);
                }
            }
            if (bridge is { } connection)
                status.Children.Add(connection == BridgeState.BrowserClosed
                    ? Ui.Body(Text.Get("Pair.BrowserClosed"), secondary: true)
                    : Ui.Mark(connection == BridgeState.Connected, Text.Get("Pair." + connection)));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Refresh product state", error);
            if (!lifetime.IsCancellationRequested)
            {
                status.Children.Clear();
                status.Children.Add(Ui.Body(Text.Get("StatusUnavailable"), secondary: true));
            }
        }
        finally { refreshingStatus = false; }
    }

    // The newest release on this PC's channel (stable, or dev on a development PC), read once per page.
    (string Channel, string Tag)? release;

    async Task RefreshReleaseAsync(Product product, StackPanel versions)
    {
        if (ReleaseFeed.Repository(product) is not { } repository) return;
        var channel = ReleaseFeed.Channel(folders);
        if (await ReleaseFeed.NewestAsync(repository, channel, lifetime.Token) is not { } tag) return;
        release = (channel, tag);
        RefreshVersions(product, versions);
    }

    /// <summary>The installed version as the product or maintenance records it, or null.</summary>
    string? InstalledVersion(Product product, MaintainedProduct? maintained) =>
        maintained?.InstalledVersion
        ?? (product == Products.Windows ? WindowsRuntime.ReadInstallation(folders)?.Version : null)
        ?? ProductUpdates.Read(product, folders).Select(state => state.Version).FirstOrDefault(version => version is not null);

    void RefreshVersions(Product product, StackPanel versions)
    {
        versions.Children.Clear();
        if (release is var (channel, tag))
        {
            var installed = InstalledVersion(product, MaintenanceUpdates.Read(folders)?.Products.FirstOrDefault(item => item.Key == product.Key));
            versions.Children.Add(Ui.Body(installed is not null && ReleaseFeed.IsNewer(tag, installed)
                ? Text.Format("Version.ReleaseNewer", channel, ReleaseFeed.Version(tag), installed)
                : Text.Format("Version.Release", channel, ReleaseFeed.Version(tag))));
        }
        var maintenance = MaintenanceUpdates.Read(folders);
        var maintained = maintenance?.Products.FirstOrDefault(item => item.Key == product.Key);
        if (maintained is null && maintenance?.State == "failed")
            versions.Children.Add(Ui.Mark(false, Text.Get("Version.Failed")));
        else if (maintenance?.State == "checking")
            versions.Children.Add(Ui.Body(Text.Get("Version.Checking"), secondary: true));
        if (maintained is not null)
        {
            if (maintained.PublishedVersion is { } published)
                versions.Children.Add(Ui.Body(Text.Format("Version.Published", published)));
            if (product != Products.Windows && maintained.InstalledVersion is { } installed)
                versions.Children.Add(Ui.Body(Text.Format("Version.Installed", installed)));
            if (maintenance!.CheckedAt is { } checkedAt)
                versions.Children.Add(Ui.Body(Text.Format(maintenance.IsFresh(DateTimeOffset.UtcNow)
                    ? "Version.Checked" : "Version.Stale", checkedAt.ToLocalTime().ToString("g")), secondary: true));
            else versions.Children.Add(Ui.Body(Text.Get("Version.Unverified"), secondary: true));
            // This is explicitly a dated observation, not a cache promoted to a live connection.
            if (maintained.LoadedVersions.Count > 0 && maintenance.IsFresh(DateTimeOffset.UtcNow))
                versions.Children.Add(Ui.Body(Text.Format("Version.Observed", string.Join(", ", maintained.LoadedVersions))));
            if (maintained.State == "failed") versions.Children.Add(Ui.Mark(false, Text.Get("Version.Failed")));
            else if (maintained.State == "pending" || maintained.Profiles.Any(profile => profile.ActivationPending))
                versions.Children.Add(Ui.Body(Text.Get("Version.Pending"), secondary: true));
            else if (maintained.State == "unsupported")
                versions.Children.Add(Ui.Body(Text.Get("Version.Unsupported"), secondary: true));
            foreach (var profile in maintained.Profiles.Where(profile => profile.ActivationPending))
            {
                var name = profile.Key switch { "claude-code" or "claude" => "Claude", "codex" => "Codex", "antigravity" => "Antigravity", _ => profile.Key };
                versions.Children.Add(Ui.Body(Text.Format("Version.ProfilePending", name), secondary: true));
            }
            if (maintained.Profiles.Any(profile => profile.State is "unknown" or "runtime-verified"))
                versions.Children.Add(Ui.Body(Text.Get("Version.GuidesUnknown"), secondary: true));
        }
        if (product == Products.Windows)
        {
            var installation = WindowsRuntime.ReadInstallation(folders);
            versions.Children.Add(installation is null ? Ui.Mark(false, Text.Get("Windows.CoreMissing"))
                : Ui.Body(Text.Format("Windows.CoreVersion", installation.Version)));
            return;
        }
        if (maintained is not null) return;
        var states = ProductUpdates.Read(product, folders);
        if (states.Count == 0) versions.Children.Add(Ui.Body(Text.Get("Version.None"), secondary: true));
        foreach (var state in states)
        {
            var edition = state.Name switch
            {
                "windows-claude" => $"{product.Name} · Claude",
                "windows-codex" => $"{product.Name} · Codex",
                "windows-antigravity" => $"{product.Name} · Antigravity",
                _ => product.Name,
            };
            // A last check older than the running version (released since) shows the running one as the latest known.
            var latest = state.Latest is { } known && state.Version is { } running && ProductState.CompareVersions(known, running) < 0
                ? running : state.Latest;
            // Never "up to date": products refresh their check date even when the check fails, and their message mixes
            // successes and diagnostics. Only the running version, or an update known to be ready.
            var current = state.Version ?? "?";
            // The channel's published release, when known, already says whether a newer version exists.
            var line = release is null && state.UpdateAvailable && latest is { } newer && newer != current ? Text.Format("Version.Newer", edition, current, newer)
                : Text.Format("Version.Current", edition, current);
            versions.Children.Add(Ui.Body(line));
            if (state.State == "failed") versions.Children.Add(Ui.Mark(false, Text.Get("Version.Failed")));
        }
    }

    void RefreshStop()
    {
        var left = EmergencyStop.SecondsLeft(folders);
        stopState.Visibility = left > 0 ? Visibility.Visible : Visibility.Collapsed;
        stopState.Text = Text.Format("StopActive", left);
    }
}
