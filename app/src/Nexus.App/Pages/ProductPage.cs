using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// A product's page: its name, state and one way in at the top, then two tabs. "Overview" is the product's own page on
/// julienpiron.fr (explanation, video), read live so the app never needs an update for it; "On this PC" is its state
/// per AI app and what is left to the user (pair Chrome, turn on USB debugging...).
/// </summary>
public sealed partial class ProductPage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly CancellationTokenSource lifetime = new();
    readonly InfoBar notice = new() { IsClosable = true, IsOpen = false };
    // "On this PC" opens with one answer, as a store would say it: everything is ready, or what is left to do.
    readonly FontIcon summaryIcon = new() { FontSize = 26, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock summaryTitle = new() { Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], TextWrapping = TextWrapping.WrapWholeWords };
    bool refreshingStatus;
    SelectorBar? tabs;
    SelectorBarItem? hereTab;

    /// <summary>--capture: the "On this PC" tab, when the product has one.</summary>
    public bool ShowHere()
    {
        if (tabs is null || hereTab is null) return false;
        tabs.SelectedItem = hereTab;
        return true;
    }
    DispatcherTimer? timer;

    public ProductPage(Product product)
    {
        Unloaded += (_, _) => { timer?.Stop(); lifetime.Cancel(); };
        var ais = AiApps.Find(folders);
        var view = Catalog.Read(product, ais, folders);

        var top = new Grid { ColumnSpacing = 24 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = Ui.Logo(product, 56);
        logo.VerticalAlignment = VerticalAlignment.Top;
        var header = Ui.Header(logo, product.Name, Catalog.About(product));
        var words = (StackPanel)header.Children[1];
        var stateHost = new ContentControl();
        var actionHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        words.Children.Add(stateHost);
        words.Children.Add(actionHost);
        top.Children.Add(header);
        // The product's picture, as on a store page: its video's poster on the site.
        if (product.Banner is { } banner)
        {
            var picture = new Border
            {
                Width = 288,
                Height = 162,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                Child = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(banner)), Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill },
            };
            Grid.SetColumn(picture, 1);
            top.Children.Add(picture);
        }
        var drawn = "";
        void DrawState()
        {
            var now = Catalog.Read(product, ais, folders);
            if (Catalog.Signature(now) == drawn) return;
            drawn = Catalog.Signature(now);
            stateHost.Content = Catalog.State(now);
            actionHost.Content = Catalog.Action(now, ais, text => { notice.Message = text; notice.IsOpen = text.Length > 0; });
        }
        DrawState();

        var layout = new Grid { Padding = new Thickness(32, 16, 32, 0), RowSpacing = 12 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(top);
        Grid.SetRow(notice, 1);
        layout.Children.Add(notice);
        Content = layout;

        // The product's page on the site, or "coming soon" for a product that has none yet.
        UIElement overview = product.PageFor(Text.French) is { } page ? Site(page)
            : Ui.Card(Ui.Body(Text.Get("NotPublic"), secondary: true), padding: 16);
        if (product.Availability != Availability.Ready)
        {
            Grid.SetRow((FrameworkElement)overview, 3);
            layout.Children.Add(overview);
            // A store app installed meanwhile shows as installed.
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => DrawState();
            timer.Start();
            return;
        }

        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { summaryIcon, summaryTitle } };
        summaryTitle.VerticalAlignment = VerticalAlignment.Center;
        var status = Ui.Column(10);
        var here = Ui.Column(16, Ui.Card(Ui.Column(16, summary, status), product.Accent, padding: 20));
        var help = Ui.Column(12);
        foreach (var (step, isHelp) in Steps.For(product, folders, ais)) (isHelp ? help : here).Children.Add(step);
        if (help.Children.Count > 0)
            here.Children.Add(new Expander { Header = Text.Get("Help"), Content = help, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var herePage = new ScrollViewer { Content = new Border { Child = here, Padding = new Thickness(0, 0, 0, 24) }, Visibility = Visibility.Collapsed };

        tabs = new SelectorBar();
        var overviewTab = new SelectorBarItem { Text = Text.Get("Tab.Overview"), IsSelected = true };
        hereTab = new SelectorBarItem { Text = Text.Get("Tab.ThisPc") };
        tabs.Items.Add(overviewTab);
        tabs.Items.Add(hereTab);
        tabs.SelectionChanged += (_, _) =>
        {
            var onHere = tabs.SelectedItem == hereTab;
            herePage.Visibility = onHere ? Visibility.Visible : Visibility.Collapsed;
            overview.Visibility = onHere ? Visibility.Collapsed : Visibility.Visible;
        };
        Grid.SetRow(tabs, 2);
        layout.Children.Add(tabs);
        Grid.SetRow((FrameworkElement)overview, 3);
        Grid.SetRow(herePage, 3);
        layout.Children.Add(overview);
        layout.Children.Add(herePage);

        _ = RefreshStatusAsync(product, status);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) =>
        {
            DrawState();
            if (herePage.Visibility == Visibility.Visible) await RefreshStatusAsync(product, status);
        };
        timer.Start();
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// The product's page of julienpiron.fr inside the app, without the site's own header, footer and install section. Any other
    /// address (another page, GitHub, a store) opens in the default browser.
    /// </summary>
    FrameworkElement Site(string page)
    {
        var web = new WebView2 { DefaultBackgroundColor = Ui.Transparent };
        var failed = Ui.Card(Ui.Column(8, Ui.Body(Text.Get("PageOffline"), secondary: true),
            new HyperlinkButton { Content = page, NavigateUri = new Uri(page), Padding = new Thickness(0) }), padding: 16);
        failed.VerticalAlignment = VerticalAlignment.Top;
        failed.Visibility = Visibility.Collapsed;
        var holder = new Grid { CornerRadius = new CornerRadius(8, 8, 0, 0), Children = { web, failed } };
        web.Loaded += async (_, _) =>
        {
            if (web.CoreWebView2 is not null) return;
            try
            {
                // The site picks its language from the browser's: the app's own, so the English app stays in English.
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync("",
                    Path.Combine(folders.LocalAppData, "NexusSystem", "WebView2"),
                    new CoreWebView2EnvironmentOptions { Language = Text.French ? "fr-FR" : "en-US" });
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2!;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                // Registered before the first page: the site's own install section never shows.
                // Hidden element by element: the site's content security policy refuses an added style sheet.
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    "document.addEventListener('DOMContentLoaded',()=>document.querySelectorAll('" + HiddenOnSite + "')" +
                    ".forEach(e=>e.style.setProperty('display','none','important')));");
                core.NavigationStarting += (_, e) =>
                {
                    if (SamePage(e.Uri, page)) return;
                    e.Cancel = true;
                    Open(e.Uri);
                };
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    Open(e.Uri);
                };
                core.NavigationCompleted += (_, e) =>
                {
                    failed.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                    web.Visibility = e.IsSuccess ? Visibility.Visible : Visibility.Collapsed;
                };
                core.Navigate(page);
            }
            catch (Exception error)
            {
                AppLog.Error("site page", error);
                failed.Visibility = Visibility.Visible;
                web.Visibility = Visibility.Collapsed;
            }
        };
        return holder;
    }

    // In the app, the site's own install section and the links to it would be a second way in, beside the app's own
    // button; the site's header, footer and family section are the app's menu already.
    const string HiddenOnSite = ".site-header,.site-footer,#installation,#ecosysteme,section[aria-labelledby=installation-title]," +
        "a[href$=\"#installation\"],a[href$=\"#ecosysteme\"]";

    // The page itself, in either language (the site may switch to the reader's language).
    static bool SamePage(string address, string page) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        Twin(uri.GetLeftPart(UriPartial.Path)).Equals(Twin(page), StringComparison.OrdinalIgnoreCase);

    static string Twin(string address) => address.Replace("julienpiron.fr/en/", "julienpiron.fr/", StringComparison.OrdinalIgnoreCase);

    static void Open(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto" or "ms-windows-store")
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }

    async Task RefreshStatusAsync(Product product, StackPanel status)
    {
        if (refreshingStatus || lifetime.IsCancellationRequested) return;
        refreshingStatus = true;
        try
        {
            // WMI and the products' own checks may take time: never block the UI, overlap refreshes or update an unloaded page.
            var (running, windows, phone, gmail) = await Task.Run(() =>
            {
                var processes = Servers.Find().Where(s => s.Product == product).ToList();
                var installation = product == Products.Windows ? WindowsRuntime.ReadInstallation(folders) : null;
                var device = product == Products.Android ? SetupChecks.ReadPhone(folders) : null;
                var signIn = product == Products.AgyMail ? SetupChecks.ReadGmail(folders) : null;
                return (processes, installation, device, signIn);
            }, lifetime.Token);
            var bridge = product == Products.Browser
                ? await BrowserBridge.StateAsync(folders, Http, lifetime.Token) : (BridgeState?)null;
            if (lifetime.IsCancellationRequested) return;
            var ais = AiApps.Find(folders);
            var installed = ProductUpdates.IsInstalled(product, ais, folders);
            var todo = false;
            status.Children.Clear();
            if (product.Device is not null)
            {
                if (!Products.LogiService(folders))
                {
                    status.Children.Add(Ui.Body(Text.Get("NeedsLogi"), secondary: true));
                    todo = true;
                }
                else
                {
                    var plugin = Declarations.DeviceInstalled(product, folders);
                    status.Children.Add(Ui.Mark(plugin, $"{product.Device} · {Text.Get(plugin ? "PluginInstalled" : "PluginMissing")}"));
                    if (plugin)
                    {
                        var keys = Declarations.KeypadKeysPlaced(folders);
                        status.Children.Add(Ui.Mark(keys, Text.Get(keys ? "Keypad.KeysPlaced" : "Keypad.KeysMissing")));
                        todo |= !keys;
                    }
                }
            }
            else if (Products.Why(product, ais, folders) is { } unfit)
            {
                status.Children.Add(Ui.Body(unfit == Unfit.NeedsApp ? Text.Get("NeedsClaudeCode") : unfit == Unfit.NeedsTwo ? Text.Get("NeedsTwo")
                    : Text.Format("OnlyFor", string.Join(", ", product.Targets.Select(AiApps.Name))), secondary: true));
                todo = true;
            }
            else
            {
                foreach (var app in ais.Where(product.Targets.Contains))
                {
                    var declared = Declarations.State(product, app, folders);
                    // A declared Windows connection without the shared engine installed cannot work: it needs repair.
                    if (product == Products.Windows && windows is null && declared == DeclarationState.Declared) declared = DeclarationState.Invalid;
                    todo |= declared is DeclarationState.Missing or DeclarationState.Invalid;
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
            // Chrome: connected, or closed (it reconnects by itself: nothing to do); open without the extension: the steps below.
            if (bridge is { } connection && connection != BridgeState.NotInstalled)
            {
                status.Children.Add(connection == BridgeState.BrowserClosed
                    ? Ui.Body(Text.Get("Pair.BrowserClosed"), secondary: true)
                    : Ui.Mark(connection == BridgeState.Connected, Text.Get("Pair." + connection)));
                todo |= connection is BridgeState.Waiting or BridgeState.Offline;
            }
            if (phone is { } device && installed)
            {
                status.Children.Add(device.State switch
                {
                    PhoneState.Connected => Ui.Mark(true, Text.Format("Phone.Connected", device.Model ?? "Android")),
                    PhoneState.Unauthorized => Ui.Mark(false, Text.Get("Phone.Unauthorized")),
                    _ => Ui.Mark(false, Text.Get("Phone.None")),
                });
                todo |= device.State != PhoneState.Connected;
            }
            if (gmail is { } signIn && installed)
            {
                if (signIn.Connected) status.Children.Add(Ui.Mark(true, Text.Format("Gmail.Connected", signIn.Account ?? "Gmail")));
                else
                {
                    // Like a connector of Antigravity: "not connected", and the one button that connects.
                    var connect = new Button { Content = Text.Get("Gmail.Connect"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                    connect.Click += (_, _) =>
                    {
                        try
                        {
                            SetupChecks.StartGmailSignIn(signIn.Executable!);
                            notice.Message = Text.Get("Gmail.Opened");
                        }
                        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                        {
                            AppLog.Error("gmail sign-in", error);
                            notice.Message = Text.Get("Gmail.Failed");
                        }
                        notice.IsOpen = true;
                    };
                    connect.IsEnabled = signIn.Executable is not null;
                    status.Children.Add(new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 12,
                        Children = { Ui.Mark(false, Text.Get("Gmail.Missing")), connect },
                    });
                    todo = true;
                }
            }
            summaryIcon.Glyph = !installed ? "" : todo ? "" : "";
            summaryIcon.Foreground = Ui.Theme(!installed ? "TextFillColorSecondaryBrush" : todo ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");
            summaryTitle.Text = Text.Get(!installed ? "Here.Missing" : todo ? "Here.Todo" : "Here.Ready");
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

}
