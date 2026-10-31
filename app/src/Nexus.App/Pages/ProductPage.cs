using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// A product's page, the same for every product: a banner in the product's color (icon, name, one sentence, the one
/// button and its state), then the whole window used: on the left the product's own page of julienpiron.fr (its video
/// and explanations, read live), on the right "On this PC" (ready, or what is left to do), what the AI apps did with it
/// (its journal, 24 hours) and the guides.
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
    DispatcherTimer? timer;
    ScrollViewer? scroller;
    FrameworkElement? here;

    /// <summary>--capture: scrolls to "On this PC".</summary>
    public bool ShowHere()
    {
        if (scroller is null || here is null) return false;
        here.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        return true;
    }

    public ProductPage(Product product)
    {
        Unloaded += (_, _) => { timer?.Stop(); lifetime.Cancel(); };
        var ais = AiApps.Find(folders);

        // The banner, in the product's color: icon, name, publisher and group, one sentence; the one button and its state.
        var stateHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Right };
        var actionHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Right };
        var drawn = "";
        void DrawState()
        {
            var now = Catalog.Read(product, ais, folders);
            if (Catalog.Signature(now) == drawn) return;
            drawn = Catalog.Signature(now);
            stateHost.Content = Catalog.State(now);
            actionHost.Content = Catalog.Action(now, ais, text => { notice.Message = text; notice.IsOpen = text.Length > 0; }, large: true);
        }
        DrawState();
        var hero = new Grid { ColumnSpacing = 24 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = Ui.Logo(product, 80);
        logo.VerticalAlignment = VerticalAlignment.Center;
        hero.Children.Add(logo);
        var about = Ui.Body(Catalog.About(product));
        about.FontSize = 15;
        var words = Ui.Column(4,
            new TextBlock { Text = product.Name, Style = (Style)Application.Current.Resources["TitleLargeTextBlockStyle"] },
            Ui.Body($"julienpiron.fr · {Catalog.Group(product.Group)}", secondary: true),
            about);
        words.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(words, 1);
        hero.Children.Add(words);
        var side = Ui.Column(10, actionHost, stateHost);
        side.VerticalAlignment = VerticalAlignment.Center;
        side.MinWidth = 220;
        Grid.SetColumn(side, 2);
        hero.Children.Add(side);
        var banner = new Border
        {
            Child = hero,
            Padding = new Thickness(24, 18, 24, 18),
            CornerRadius = new CornerRadius(12),
            Background = Ui.Tint(product.Accent),
            BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
        };

        // Left: the product's page of the site (video, explanations); without one, its sentence and "coming soon".
        FrameworkElement left = product.PageFor(Text.French) is { } address ? Site(address)
            : Section(Text.Get("Section.About"), Ui.Column(12, Ui.Body(Catalog.About(product)), Ui.Body(Text.Get("Here.Soon"), secondary: true)));
        left.VerticalAlignment = left is Border ? VerticalAlignment.Top : VerticalAlignment.Stretch;

        // Right: "On this PC", then the journal, then the guides, in their own scroll.
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { summaryIcon, summaryTitle } };
        summaryTitle.VerticalAlignment = VerticalAlignment.Center;
        summaryTitle.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        summaryIcon.FontSize = 20;
        var status = Ui.Column(10);
        var thisPc = Ui.Column(14, summary, status);
        var help = Ui.Column(12);
        foreach (var (step, isHelp) in Steps.For(product, folders, ais)) (isHelp ? help : thisPc).Children.Add(step);
        var panel = Ui.Column(16, here = Section(Text.Get("Tab.ThisPc"), thisPc));
        if (Activity(product) is { } activity) panel.Children.Add(Section(Text.Get("Activity"), activity));
        if (help.Children.Count > 0) panel.Children.Add(Section(Text.Get("Help"), help));
        var sideScroll = new ScrollViewer { Content = new Border { Child = panel, Padding = new Thickness(0, 0, 12, 24) } };

        var body = new Grid { ColumnSpacing = 20 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        body.Children.Add(left);
        Grid.SetColumn(sideScroll, 1);
        body.Children.Add(sideScroll);

        var layout = new Grid { Padding = new Thickness(24, 16, 12, 0), RowSpacing = 16 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(banner);
        Grid.SetRow(notice, 1);
        layout.Children.Add(notice);
        Grid.SetRow(body, 2);
        layout.Children.Add(body);
        Content = layout;
        scroller = sideScroll;

        _ = RefreshStatusAsync(product, status);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) =>
        {
            DrawState();
            await RefreshStatusAsync(product, status);
        };
        timer.Start();
    }

    /// <summary>A titled card of the page, like the sections of a store page.</summary>
    static Border Section(string title, UIElement content)
    {
        var card = Ui.Card(Ui.Column(12, Ui.Subtitle(title), content), padding: 20);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    /// <summary>The connected Gmail account, as a connector shows it: its initial, its address, and "Use another account".</summary>
    FrameworkElement GmailAccount(GmailSignIn signIn)
    {
        var account = signIn.Account ?? "Gmail";
        var initial = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
            Background = Ui.Theme("AccentFillColorDefaultBrush"),
            Child = new TextBlock
            {
                Text = account[..1].ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Theme("TextOnAccentFillColorPrimaryBrush"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            },
        };
        var words = Ui.Column(0, Ui.Strong(account), Ui.Body(Text.Get("Gmail.Primary"), secondary: true));
        words.VerticalAlignment = VerticalAlignment.Center;
        var other = new HyperlinkButton { Content = Text.Get("Gmail.Other"), Padding = new Thickness(0) };
        other.Click += (_, _) =>
        {
            if (signIn.Executable is not { } executable) return;
            try
            {
                SetupChecks.StartGmailSignIn(executable);
                notice.Message = Text.Get("Gmail.Opened");
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                AppLog.Error("gmail sign-in", error);
                notice.Message = Text.Get("Gmail.Failed");
            }
            notice.IsOpen = true;
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { initial, words } };
        return Ui.Card(Ui.Column(10, Ui.Strong(Text.Get("Gmail.Accounts")), row, other), padding: 14);
    }

    /// <summary>What the AI apps did with the product in the last 24 hours (its journal), newest first; none without one.</summary>
    FrameworkElement? Activity(Product product)
    {
        if (product.JournalFolder is null) return null;
        var entries = Journal.Read(product, folders, DateTimeOffset.UtcNow.AddHours(-24), limit: 200);
        if (entries.Count == 0) return Ui.Body(Text.Get("NoActivity"), secondary: true);
        var (count, verified, failed, _) = Journal.Summarize(entries);
        var column = Ui.Column(10, Ui.Body(Text.Format("ActivitySummary", count, verified)));
        if (failed > 0) column.Children.Add(Ui.Body(Text.Format("ActivityFailed", failed), secondary: true));
        var list = Ui.Column(6);
        foreach (var entry in entries.Take(25))
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = entry.At.ToLocalTime().ToString("HH:mm"), Foreground = Ui.Theme("TextFillColorSecondaryBrush"), FontSize = 12.5 });
            var app = AiApps.All.FirstOrDefault(a => AiApps.Key(a).StartsWith(entry.Agent, StringComparison.OrdinalIgnoreCase));
            UIElement who = app is { } known ? Ui.AiLogo(known, 16) : new TextBlock { Text = entry.Agent, FontSize = 12.5 };
            Grid.SetColumn((FrameworkElement)who, 1);
            row.Children.Add(who);
            // Failed (✗) in red; done but not read back (~) in grey; done in the text color.
            var what = new TextBlock
            {
                Text = $"{entry.Action} · {entry.App}{(entry.Error is null ? "" : " · " + entry.Error)}",
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Ui.Theme(entry.Outcome switch { "✗" => "SystemFillColorCriticalBrush", "~" => "TextFillColorSecondaryBrush", _ => "TextFillColorPrimaryBrush" }),
            };
            ToolTipService.SetToolTip(what, what.Text);
            Grid.SetColumn(what, 2);
            row.Children.Add(what);
            list.Children.Add(row);
        }
        column.Children.Add(list);
        return column;
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
            if (product.Availability == Availability.NotPublic)
                status.Children.Add(Ui.Body(Text.Get("Here.Soon"), secondary: true));
            else if (product.StoreUrl is not null)
            {
                if (installed) status.Children.Add(Ui.Mark(true, Text.Get("Here.FromStore")));
            }
            else if (product.Device is not null)
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
                // As the official connectors show it: the connected account, and a way to sign in with another one.
                if (signIn.Connected) status.Children.Add(GmailAccount(signIn));
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
            if (product.Availability == Availability.NotPublic)
            {
                summaryIcon.Glyph = "";
                summaryIcon.Foreground = Ui.Theme("TextFillColorSecondaryBrush");
                summaryTitle.Text = Text.Get("NotPublic");
                return;
            }
            summaryIcon.Glyph = !installed ? "" : todo ? "" : "";
            summaryIcon.Foreground = Ui.Theme(!installed ? "TextFillColorSecondaryBrush" : todo ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");
            summaryTitle.Text = Text.Get(!installed ? (product.StoreUrl is null ? "Here.Missing" : "Here.MissingStore") : todo ? "Here.Todo" : "Here.Ready");
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
