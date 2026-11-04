using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// The hub's home, like the Microsoft Store's: a banner that shows the products one after the other with two tiles
/// beside it, the AI apps found, then one panel per group (AI, Adobe, Windows) whose rows give each product's state on
/// this PC (installed or not, up to date or not) and its one way in (the user's AI app or a store), and a button that
/// has the AI install them all. Updates have their own page, as in the Store.
/// </summary>
public sealed partial class HomePage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly IReadOnlyList<AiApp> found;
    readonly StackPanel groups = new() { Spacing = 20 };
    readonly InfoBar notice = new() { IsClosable = true, IsOpen = false };
    readonly Button installAll = new() { Content = Text.Get("InstallAllAi"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly CancellationTokenSource lifetime = new();
    // At the top, as soon as any installed product has a newer version: which ones, and the way to the Updates page.
    readonly InfoBar updates = new() { IsClosable = false, IsOpen = false, Severity = InfoBarSeverity.Warning };

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

        var see = new Button { Content = Text.Get("Updates.See"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        see.Click += (_, _) => MainWindow.Instance?.OpenUpdates();
        updates.ActionButton = see;
        var page = Ui.Column(16, top, updates);
        if (Hero() is { } hero) page.Children.Add(hero);
        page.Children.Add(aiLine);
        page.Children.Add(notice);
        page.Children.Add(groups);
        Content = Ui.Page(page);
        Fill();
        // Like the Store, a row follows an install as it happens: the products write their state as they go.
        timer.Tick += (_, _) =>
        {
            if (Signatures() != shown) Fill();
        };
        timer.Start();
        // The published versions are read once per run (GitHub's release feeds): the rows then say "up to date" or not.
        if (!Catalog.HasLatest) _ = RefreshAsync();
    }

    const double HeroHeight = 380;

    /// <summary>
    /// The Store's banner: on the left the products with a picture in turn (its video's poster across the whole slide,
    /// its name, sentence and button over it), turning every few seconds; on the right two tiles that open a product at
    /// once. The arrows or the dots move the slides by hand.
    /// </summary>
    FrameworkElement? Hero()
    {
        var featured = Catalog.Shown.Products.Where(product => product.Banner is not null).ToList();
        if (featured.Count == 0) return null;
        var flip = new FlipView { Height = HeroHeight, CornerRadius = new CornerRadius(12), Background = Ui.Theme("SolidBackgroundFillColorBaseBrush") };
        foreach (var product in featured)
        {
            var slide = Picture(product);
            var see = new Button { Content = Text.Get("Hero.See"), Style = (Style)Application.Current.Resources["AccentButtonStyle"], Padding = new Thickness(22, 7, 22, 7) };
            see.Click += (_, _) => MainWindow.Instance?.Open(product);
            // The poster carries the product's own words in its middle: only its icon, name and button at the bottom.
            var name = new TextBlock { Text = product.Name, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Foreground = White(0xFF), VerticalAlignment = VerticalAlignment.Center };
            var logo = Ui.Logo(product, 44);
            see.VerticalAlignment = VerticalAlignment.Center;
            see.Margin = new Thickness(12, 0, 0, 0);
            var words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { logo, name, see } };
            words.Margin = new Thickness(28, 0, 28, 24);
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.HorizontalAlignment = HorizontalAlignment.Left;
            slide.Children.Add(words);
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
        if (featured.Count < 3) return Ui.Column(8, flip, pager);

        // The two tiles beside the banner, as the Store's: the last products of the banner, each opening its page.
        var right = new Grid { RowSpacing = 12 };
        foreach (var product in featured.TakeLast(2))
        {
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var picture = Picture(product);
            picture.CornerRadius = new CornerRadius(12);
            picture.Children.Add(new TextBlock
            {
                Text = product.Name,
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                Foreground = White(0xFF),
                Margin = new Thickness(20, 0, 20, 16),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            var tile = Opens(picture, product);
            tile.CornerRadius = new CornerRadius(12);
            Grid.SetRow(tile, right.RowDefinitions.Count - 1);
            right.Children.Add(tile);
        }
        var banner = TwoColumns();
        banner.ColumnSpacing = 12;
        banner.Height = HeroHeight;
        banner.Children.Add(flip);
        Grid.SetColumn(right, 1);
        banner.Children.Add(right);
        var under = TwoColumns();
        under.ColumnSpacing = 12;
        under.Children.Add(pager);
        return Ui.Column(8, banner, under);
    }

    static Grid TwoColumns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        return grid;
    }

    /// <summary>A product's poster across the whole area (cropped to fill it), darkened at the bottom left for the words over it.</summary>
    static Grid Picture(Product product)
    {
        var picture = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(product.Banner!)),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var shade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 1), EndPoint = new Windows.Foundation.Point(0.8, 0.1) };
        shade.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0xD8, 0, 0, 0), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x00, 0, 0, 0), Offset = 1 });
        return new Grid { Background = Ui.Tint(product.Accent), Children = { picture, new Border { Background = shade } } };
    }

    static SolidColorBrush White(byte alpha) => new(Windows.UI.Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// Like a store tile, the whole element opens the product's page, with the mouse or with Tab then Enter or Space; its
    /// button does its own thing.
    /// </summary>
    static ContentControl Opens(FrameworkElement content, Product product, Action<bool>? hover = null)
    {
        var tile = new ContentControl
        {
            Content = content,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        tile.PointerEntered += (_, _) => hover?.Invoke(true);
        tile.PointerExited += (_, _) => hover?.Invoke(false);
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

    /// <summary>As the Store's "Trending apps": one panel per group, its title and a line, then compact rows in columns.</summary>
    void Fill()
    {
        shown = Signatures();
        var late = Catalog.Shown.Products.Select(product => Catalog.Read(product, found, folders)).Where(view => view.UpdateAvailable).Select(view => view.Product.Name).ToList();
        updates.Title = late.Count == 1 ? Text.Get("Updates.OneAvailable") : Text.Format("Updates.Available", late.Count);
        updates.Message = string.Join(", ", late);
        updates.IsOpen = late.Count > 0;
        groups.Children.Clear();
        foreach (var group in Catalog.Shown.Groups)
        {
            var rows = Catalog.Shown.Products.Where(product => product.Group == group.Key).Select(product => Row(Catalog.Read(product, found, folders))).ToList();
            if (rows.Count == 0) continue;
            var line = new Border { Height = 1, Background = Ui.Theme("DividerStrokeColorDefaultBrush"), Margin = new Thickness(0, 4, 0, 4) };
            groups.Children.Add(Ui.Card(Ui.Column(8, Ui.Subtitle(Catalog.Group(group.Key)), line, Ui.Flow(rows, minWidth: 420, spacing: 8)), padding: 24));
        }
    }

    /// <summary>A product as a row of the Store's lists: its icon, its name and sentence, its state and its one button.</summary>
    FrameworkElement Row(ProductView view)
    {
        var product = view.Product;
        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(12), MinHeight = 104 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = Ui.Logo(product, 72);
        logo.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(logo);
        var about = Ui.Body(Catalog.About(product), secondary: true);
        about.MaxLines = 2;
        about.TextTrimming = TextTrimming.CharacterEllipsis;
        var bottom = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 6, 0, 0), MinHeight = 32 };
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
        var words = Ui.Column(2, Ui.Strong(product.Name), about, bottom);
        words.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var face = new Border { Child = row, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Ui.Transparent) };
        return Opens(face, product, on => face.Background = on ? Ui.Theme("SubtleFillColorSecondaryBrush") : new SolidColorBrush(Ui.Transparent));
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
