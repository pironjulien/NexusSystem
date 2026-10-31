using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// The hub's home, like the Microsoft Store's: a banner that shows the products one after the other, the AI apps found,
/// then the products by group (AI, Adobe, Windows), each with its state on this PC (installed or not, up to date or
/// not) and its one way in (the user's AI app or a store), and a button that has the AI install them all. Updates have
/// their own page, as in the Store.
/// </summary>
public sealed partial class HomePage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly IReadOnlyList<AiApp> found;
    readonly StackPanel groups = new() { Spacing = 28 };
    readonly InfoBar notice = new() { IsClosable = true, IsOpen = false };
    readonly Button installAll = new() { Content = Text.Get("InstallAllAi"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly CancellationTokenSource lifetime = new();

    public HomePage()
    {
        Unloaded += (_, _) => { timer.Stop(); lifetime.Cancel(); };
        found = AiApps.Find(folders);

        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Header(Ui.Logo("hub", 48), "NexusSystem", Text.Get("Tagline")));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { installAll } };
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

        var page = Ui.Column(16, top);
        if (Hero() is { } hero) page.Children.Add(hero);
        page.Children.Add(aiLine);
        page.Children.Add(notice);
        page.Children.Add(groups);
        Content = Ui.Page(page);
        Fill();
        // Like the Store, a card follows an install as it happens: the products write their state as they go.
        timer.Tick += (_, _) =>
        {
            if (Signatures() != shown) Fill();
        };
        timer.Start();
        // The published versions are read once per run (GitHub's release feeds): the cards then say "up to date" or not.
        if (!Catalog.HasLatest) _ = RefreshAsync();
    }

    /// <summary>
    /// The Store's banner: each product with a picture in turn (its video's poster), its name, its sentence and a button
    /// to its page; it turns every few seconds, and the arrows or the dots move it by hand.
    /// </summary>
    FrameworkElement? Hero()
    {
        var featured = Catalog.Shown.Products.Where(product => product.Banner is not null).ToList();
        if (featured.Count == 0) return null;
        var flip = new FlipView { Height = 340, CornerRadius = new CornerRadius(12), Background = Ui.Theme("CardBackgroundFillColorDefaultBrush") };
        foreach (var product in featured)
        {
            // The picture whole (never cropped), on the dark of the posters; the product's words beside it.
            var slide = new Grid { Background = Ui.Theme("SolidBackgroundFillColorBaseBrush") };
            slide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            slide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var words = Ui.Column(10,
                Ui.Logo(product, 64),
                new TextBlock { Text = product.Name, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] },
                Ui.Body(Catalog.About(product), secondary: true));
            ((Image)words.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
            var see = new Button { Content = Text.Get("Hero.See"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            see.Click += (_, _) => MainWindow.Instance?.Open(product);
            words.Children.Add(see);
            var panel = new Border { Child = words, Padding = new Thickness(36, 28, 28, 28), Background = Ui.Tint(product.Accent) };
            words.VerticalAlignment = VerticalAlignment.Center;
            slide.Children.Add(panel);
            var picture = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(product.Banner!)),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(picture, 1);
            slide.Children.Add(picture);
            flip.Items.Add(slide);
        }
        var pager = new PipsPager { NumberOfPages = featured.Count, HorizontalAlignment = HorizontalAlignment.Center };
        pager.SelectedIndexChanged += (_, _) => flip.SelectedIndex = pager.SelectedPageIndex;
        flip.SelectionChanged += (_, _) => pager.SelectedPageIndex = Math.Max(0, flip.SelectedIndex);
        var turn = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        turn.Tick += (_, _) => flip.SelectedIndex = (flip.SelectedIndex + 1) % featured.Count;
        flip.Loaded += (_, _) => turn.Start();
        flip.Unloaded += (_, _) => turn.Stop();
        // A pointer on the banner keeps the slide still, as in the Store.
        flip.PointerEntered += (_, _) => turn.Stop();
        flip.PointerExited += (_, _) => turn.Start();
        return Ui.Column(8, flip, pager);
    }

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

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    string shown = "";

    string Signatures() => string.Join(";", Catalog.Shown.Products.Select(product => Catalog.Signature(Catalog.Read(product, found, folders))));

    void Fill()
    {
        shown = Signatures();
        groups.Children.Clear();
        foreach (var group in Catalog.Shown.Groups)
        {
            var cards = Catalog.Shown.Products.Where(product => product.Group == group.Key).Select(product => Card(Catalog.Read(product, found, folders))).ToList();
            groups.Children.Add(Ui.Column(12, Ui.Subtitle(Catalog.Group(group.Key)), Ui.Flow(cards, minWidth: 460, spacing: 16)));
        }
    }

    FrameworkElement Card(ProductView view)
    {
        var product = view.Product;
        // As a store tile: icon, name and group on top, two lines of text, then a line with the state and the button.
        var head = new Grid { ColumnSpacing = 14 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = Ui.Logo(product, 56);
        head.Children.Add(logo);
        var names = Ui.Column(2,
            new TextBlock { Text = product.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], FontSize = 17 },
            Ui.Body(Catalog.Group(product.Group), secondary: true));
        names.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(names, 1);
        head.Children.Add(names);

        var about = Ui.Body(Catalog.About(product), secondary: true);
        about.MaxLines = 2;
        about.Height = 42;
        about.TextTrimming = TextTrimming.CharacterEllipsis;

        var bottom = new Grid { ColumnSpacing = 12, MinHeight = 34 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var state = Catalog.State(view);
        state.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(state);
        if (Catalog.Action(view, found, Say) is { } action)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 1);
            bottom.Children.Add(action);
        }
        var grid = Ui.Column(14, head, about, bottom);

        var card = Ui.Card(grid, padding: 16);
        card.MinHeight = 132;
        card.BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush");
        // Like a store tile: the whole card opens the product's page, with the mouse or with Tab then Enter or Space;
        // its button does its own thing.
        var tile = new ContentControl
        {
            Content = card,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        tile.PointerEntered += (_, _) => card.BorderBrush = Ui.Accent(product.Accent);
        tile.PointerExited += (_, _) => card.BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush");
        tile.Tapped += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source is not null && source != tile; source = VisualTreeHelper.GetParent(source))
                if (source is ButtonBase) return;
            MainWindow.Instance?.Open(product);
        };
        tile.KeyDown += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, tile) || e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
            e.Handled = true;
            MainWindow.Instance?.Open(product);
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, $"{product.Name}. {Catalog.About(product)}");
        return tile;
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
}
