using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// The hub's home, like a store library: the AI apps found, then the products by group (AI, Adobe, Windows), each with
/// its state on this PC (installed or not, up to date or not) and its one way in (the user's AI app or a store), a
/// button that has the AI install them all, and one that checks for updates.
/// </summary>
public sealed partial class HomePage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly IReadOnlyList<AiApp> found;
    readonly StackPanel groups = new() { Spacing = 28 };
    readonly InfoBar notice = new() { IsClosable = true, IsOpen = false };
    readonly Button checkUpdates = new() { Content = Icon("", Text.Get("CheckUpdates")) };
    readonly Button installAll = new() { Content = Text.Get("InstallAllAi"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly CancellationTokenSource lifetime = new();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public HomePage()
    {
        Unloaded += (_, _) => lifetime.Cancel();
        found = AiApps.Find(folders);

        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Header(Ui.Logo("hub", 48), "NexusSystem", Text.Get("Tagline")));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { installAll, checkUpdates } };
        Grid.SetColumn(actions, 1);
        top.Children.Add(actions);

        var aiLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var aiLabel = Ui.Body(Text.Get("AiFound"), secondary: true);
        aiLabel.VerticalAlignment = VerticalAlignment.Center;
        aiLine.Children.Add(aiLabel);
        foreach (var app in found) aiLine.Children.Add(Ui.AiPill(app, declared: true, compact: true));
        if (found.Count == 0) aiLine.Children.Add(Ui.Body(Text.Get("NoAi")));

        Catalog.HandOff(installAll, AllKeys, found, Say);
        installAll.IsEnabled = found.Count > 0;
        checkUpdates.Click += async (_, _) => await CheckUpdatesAsync();

        Content = Ui.Page(Ui.Column(16, top, aiLine, notice, groups));
        Fill();
        // The published versions are read once per run (GitHub's release feeds): the cards then say "up to date" or not.
        if (!Catalog.HasLatest) _ = RefreshAsync();
    }

    static StackPanel Icon(string glyph, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
    };

    async Task RefreshAsync()
    {
        try
        {
            await Catalog.RefreshLatestAsync(folders, lifetime.Token);
            if (!lifetime.IsCancellationRequested) Fill();
        }
        catch (Exception error) when (error is not OperationCanceledException) { AppLog.Error("releases", error); }
        catch (OperationCanceledException) { }
    }

    void Fill()
    {
        groups.Children.Clear();
        foreach (var group in Catalog.Shown.Groups)
        {
            var cards = Catalog.Shown.Products.Where(product => product.Group == group.Key).Select(product => Card(Catalog.Read(product, found, folders))).ToList();
            groups.Children.Add(Ui.Column(12, Ui.Subtitle(Catalog.Group(group.Key)), Ui.Flow(cards)));
        }
    }

    FrameworkElement Card(ProductView view)
    {
        var product = view.Product;
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = Ui.Logo(product, 56);
        logo.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRowSpan(logo, 3);
        grid.Children.Add(logo);

        var title = new TextBlock { Text = product.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], FontSize = 16 };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var about = Ui.Body(Catalog.About(product), secondary: true);
        about.FontSize = 12.5;
        about.MaxLines = 2;
        about.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(about, 1);
        Grid.SetRow(about, 1);
        grid.Children.Add(about);

        var bottom = new Grid { ColumnSpacing = 8 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var state = Catalog.State(view);
        state.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(state);
        if (Catalog.Action(view, found, Say) is { } action)
        {
            Grid.SetColumn(action, 1);
            bottom.Children.Add(action);
        }
        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);

        var card = Ui.Card(grid, padding: 16);
        card.MinHeight = 132;
        card.BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush");
        // Like a store tile: the whole card opens the product's page; its button does its own thing.
        card.PointerEntered += (_, _) => card.BorderBrush = Ui.Accent(product.Accent);
        card.PointerExited += (_, _) => card.BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush");
        card.Tapped += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source is not null && source != card; source = VisualTreeHelper.GetParent(source))
                if (source is ButtonBase) return;
            MainWindow.Instance?.Open(product);
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, product.Name);
        return card;
    }

    void Say(string text, InfoBarSeverity severity)
    {
        if (text.Length == 0) return;
        notice.Message = text;
        notice.Severity = severity;
        notice.IsOpen = true;
    }

    void Say(string text) => Say(text, InfoBarSeverity.Success);

    /// <summary>Every AI product that fits this PC and is missing or out of date (all of them when none is).</summary>
    IReadOnlyList<string> AllKeys()
    {
        var views = Catalog.Shown.Products.Where(product => product.Availability == Availability.Ready)
            .Select(product => Catalog.Read(product, found, folders)).Where(view => view.Unfit is null).ToList();
        var wanted = views.Where(view => !view.Installed || view.UpdateAvailable).ToList();
        return (wanted.Count > 0 ? wanted : views).Select(view => view.Product.Key).ToList();
    }

    /// <summary>
    /// Like the Store's "Get updates": reads the published versions again and asks the products that update themselves
    /// (NexusSystem's maintenance, NexusRelay, NexusBrowser) to do it now. Never closes an AI session.
    /// </summary>
    async Task CheckUpdatesAsync()
    {
        checkUpdates.IsEnabled = false;
        Say(Text.Get("Updates.Checking"), InfoBarSeverity.Informational);
        try
        {
            await Catalog.RefreshLatestAsync(folders, lifetime.Token);
            var started = false;
            var maintenanceAsked = false;
            foreach (var product in Catalog.Shown.Products.Where(product => ProductUpdates.IsInstalled(product, found, folders) && ProductUpdates.CanRequest(product, folders)))
            {
                // One maintenance run covers every product it manages.
                var managed = MaintenanceUpdates.Supports(product) && MaintenanceUpdates.IsInstalled(folders);
                if (managed && maintenanceAsked) continue;
                maintenanceAsked |= managed;
                started |= await ProductUpdates.RequestManagedAsync(product, folders, Http, lifetime.Token) == UpdateRequest.Started;
            }
            if (lifetime.IsCancellationRequested) return;
            Fill();
            var late = Catalog.Shown.Products.Select(product => Catalog.Read(product, found, folders)).Where(view => view.UpdateAvailable).Select(view => view.Product.Name).ToList();
            if (late.Count > 0) Say(Text.Format("Updates.Some", string.Join(", ", late)), InfoBarSeverity.Warning);
            else Say(Text.Get(started ? "Updates.Started" : "Updates.UpToDate"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLog.Error("updates", error);
            Say(Text.Get("Updates.Failed"), InfoBarSeverity.Error);
        }
        finally { checkUpdates.IsEnabled = true; }
    }
}
