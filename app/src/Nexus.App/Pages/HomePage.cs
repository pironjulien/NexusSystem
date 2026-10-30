using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexus.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Nexus.App.Pages;

/// <summary>
/// The hub's home: AI apps found (with their official logos), 2-column à-la-carte product cards, a button to copy the
/// tailored AI installation prompt alongside the direct 1-click Install button, live progress, and the ✓ / ✗ table.
/// </summary>
public sealed partial class HomePage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly Dictionary<string, CheckBox> picks = [];
    readonly Dictionary<string, Border> cards = [];
    readonly StackPanel steps = new() { Spacing = 6 };
    readonly StackPanel results = new() { Spacing = 10 };
    readonly TextBlock log = new() { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    readonly Button copyPrompt = new() { Content = Text.Get("CopyPrompt"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly Button install = new() { Content = Text.Get("Install") };
    readonly ProgressRing ring = new() { Width = 20, Height = 20, IsActive = false };
    readonly TextBlock promptNotice = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    MainWindow? window;
    IReadOnlyList<AiApp> found = [];
    bool busy;

    public HomePage()
    {
        window = MainWindow.Instance;
        found = AiApps.Find(folders);

        var aiRow = new Grid { ColumnSpacing = 16 };
        aiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        aiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var aiLabel = Ui.Strong(Text.Get("AiFound"));
        aiLabel.VerticalAlignment = VerticalAlignment.Center;
        aiRow.Children.Add(aiLabel);

        var aiLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var app in found)
            aiLine.Children.Add(Ui.AiPill(app, declared: true));
        UIElement aiRight = found.Count > 0 ? aiLine : Ui.Body(Text.Get("NoAi"), secondary: true);
        Grid.SetColumn((FrameworkElement)aiRight, 1);
        aiRow.Children.Add(aiRight);

        var anyUninstalledFit = Products.All.Any(p =>
            p.Availability == Availability.Ready &&
            Products.Why(p, found, folders) is null &&
            !(Packaging.IsPackaged && ProductUpdates.IsInstalled(p, found, folders)));

        var productsGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        productsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        productsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var allProducts = Products.All;
        var rowCount = (allProducts.Count + 1) / 2;
        for (var r = 0; r < rowCount; r++)
            productsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < allProducts.Count; i++)
        {
            var card = ProductCard(allProducts[i], anyUninstalledFit);
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
            productsGrid.Children.Add(card);
        }

        var selectAll = new HyperlinkButton { Content = Text.Get("SelectAll"), Padding = new Thickness(6, 2, 6, 2) };
        selectAll.Click += (_, _) => SetAllPicks(true);
        var selectNone = new HyperlinkButton { Content = Text.Get("SelectNone"), Padding = new Thickness(6, 2, 6, 2) };
        selectNone.Click += (_, _) => SetAllPicks(false);

        var productsHeader = new Grid { ColumnSpacing = 16 };
        productsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        productsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(Ui.Subtitle(Text.Get("Products")));
        var hint = Ui.Body(Text.Get("ProductsHint"), secondary: true);
        hint.FontSize = 12.5;
        hint.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(hint);
        productsHeader.Children.Add(titleRow);
        var bulkActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { selectAll, selectNone } };
        Grid.SetColumn(bulkActions, 1);
        productsHeader.Children.Add(bulkActions);

        copyPrompt.Click += (_, _) => CopyAiPrompt();
        install.Click += async (_, _) =>
        {
            try
            {
                await InstallAsync();
            }
            catch (Exception error)
            {
                AppLog.Error("install", error);
                busy = false;
                ring.IsActive = false;
                RefreshButtons();
                results.Children.Add(Ui.Body(Text.Get("Failed") + " (" + error.Message + ")"));
            }
        };
        RefreshButtons();

        // The action bar stays in sight at the bottom of the window, whatever the scroll position.
        var bar = new Border
        {
            Padding = new Thickness(32, 12, 32, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = Ui.Theme("CardStrokeColorDefaultBrush"),
            Background = Ui.Theme("LayerFillColorDefaultBrush"),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { copyPrompt, install, ring, promptNotice } },
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(Ui.Page(Ui.Column(12,
            Ui.Header(Ui.Logo("hub", 48), "NexusSystem", Text.Get("Tagline")),
            Ui.Card(aiRow, padding: 12),
            productsHeader,
            productsGrid,
            steps,
            results)));
        Grid.SetRow(bar, 1);
        layout.Children.Add(bar);
        Content = layout;
    }

    FrameworkElement ProductCard(Product product, bool anyUninstalledFit)
    {
        var about = Text.Get(char.ToUpperInvariant(product.Key[0]) + product.Key[1..] + ".About");
        var unfit = product.Availability == Availability.Ready ? Products.Why(product, found, folders) : null;
        var installed = Packaging.IsPackaged && unfit is null && ProductUpdates.IsInstalled(product, found, folders);
        var fits = product.Availability == Availability.Ready && unfit is null && (!installed || !anyUninstalledFit);
        var targetAis = product.Targets.Where(found.Contains).ToList();
        var state = product.Availability switch
        {
            Availability.Ready when installed => Text.Get("InstalledSelfUpdating"),
            Availability.Ready when unfit is Unfit.NeedsDevice => Text.Get("NeedsLogi"),
            Availability.Ready when unfit is Unfit.NeedsApp => Text.Get("NeedsClaudeCode"),
            Availability.Ready when unfit is Unfit.NeedsTwo => Text.Get("NeedsTwo"),
            Availability.Ready when unfit is Unfit.NoTarget => Text.Format("OnlyFor", string.Join(", ", product.Targets.Select(AiApps.Name))),
            Availability.Ready when product.Device is not null => Text.Format("ForDevice", product.Device),
            Availability.Ready when targetAis.Count == 0 => Text.Format("ForApps", string.Join(", ", product.Targets.Select(AiApps.Name))),
            Availability.Ready => "",
            Availability.Later => Text.Get("Later"),
            Availability.OtherStore => Text.Get("OtherStore"),
            _ => Text.Get("NotPublic"),
        };

        var topRow = new Grid { ColumnSpacing = 10 };
        topRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        topRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var pick = new CheckBox
        {
            IsChecked = fits,
            IsEnabled = product.Availability == Availability.Ready,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRowSpan(pick, 2);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pick, product.Name);
        picks[product.Key] = pick;
        topRow.Children.Add(pick);

        var logo = Ui.Logo(product.Key, 44);
        logo.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(logo, 1);
        Grid.SetRowSpan(logo, 2);
        topRow.Children.Add(logo);

        var title = Ui.Strong(product.Name);
        title.TextWrapping = TextWrapping.Wrap;
        var titleStack = Ui.Column(2, title);
        if (state.Length > 0)
            titleStack.Children.Add(new TextBlock { Text = state, FontSize = 11.5, TextWrapping = TextWrapping.WrapWholeWords, Foreground = Ui.AccentText(product.Accent) });
        titleStack.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(titleStack, 2);
        topRow.Children.Add(titleStack);

        var open = new HyperlinkButton { Content = Text.Get("OpenPage"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2) };
        open.Click += (_, _) => window?.Open(product);
        Grid.SetColumn(open, 2);
        Grid.SetRow(open, 1);
        topRow.Children.Add(open);

        var desc = Ui.Body(about, secondary: true);
        desc.FontSize = 12.5;
        var cardContent = Ui.Column(6, topRow, desc);

        if (targetAis.Count > 0)
        {
            var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var app in targetAis)
            {
                var declared = Declarations.State(product, app, folders) == DeclarationState.Declared;
                pills.Children.Add(Ui.AiPill(app, declared, compact: true));
            }
            cardContent.Children.Add(pills);
        }

        var card = Ui.Card(cardContent, fits ? product.Accent : null, padding: 12);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        cards[product.Key] = card;

        void SyncCardBorder()
        {
            var on = pick.IsChecked == true;
            card.BorderBrush = on ? Ui.Accent(product.Accent) : Ui.Theme("CardStrokeColorDefaultBrush");
            card.BorderThickness = new Thickness(on ? 1.5 : 1);
            promptNotice.Visibility = Visibility.Collapsed;
            RefreshButtons();
        }
        pick.Checked += (_, _) => SyncCardBorder();
        pick.Unchecked += (_, _) => SyncCardBorder();
        return card;
    }

    List<string> ChosenKeys() => picks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();

    void SetAllPicks(bool on)
    {
        foreach (var (_, box) in picks)
            if (box.IsEnabled) box.IsChecked = on;
    }

    void RefreshButtons()
    {
        var anyChosen = ChosenKeys().Count > 0;
        copyPrompt.IsEnabled = !busy && anyChosen;
        install.IsEnabled = !busy && found.Count > 0 && anyChosen;
    }

    void CopyAiPrompt()
    {
        var prompt = Products.AiPrompt(ChosenKeys(), Text.French);
        if (prompt.Length == 0) return;
        try
        {
            var package = new DataPackage();
            package.SetText(prompt);
            Clipboard.SetContent(package);
            promptNotice.Text = Text.Get("PromptCopied");
            promptNotice.Foreground = Ui.Theme("SystemFillColorSuccessBrush");
            promptNotice.Visibility = Visibility.Visible;
        }
        catch (Exception error)
        {
            AppLog.Error("prompt", error);
        }
    }

    /// <summary>--capture --frames: a product ticked or not, as the user would.</summary>
    public void Pick(string key, bool on) => picks[key].IsChecked = on;

    /// <summary>--capture --frames: the screen while the pack runs, without running it.</summary>
    public void ShowInstalling()
    {
        busy = true;
        RefreshButtons();
        ring.IsActive = true;
        steps.Children.Clear();
        steps.Children.Add(Ui.Strong(Text.Get("Installing")));
    }

    async Task InstallAsync()
    {
        var chosen = ChosenKeys();
        if (chosen.Count == 0) return;
        busy = true;
        promptNotice.Visibility = Visibility.Collapsed;
        RefreshButtons();
        ring.IsActive = true;
        steps.Children.Clear();
        results.Children.Clear();
        log.Text = "";
        steps.Children.Add(Ui.Strong(Text.Get("Installing")));
        steps.StartBringIntoView();
        var cells = new System.Collections.Concurrent.ConcurrentQueue<PackEvent>();
        var lines = new System.Text.StringBuilder();
        var dispatcher = DispatcherQueue;
        var code = await PackRunner.RunAsync(chosen, dryRun: false,
            line => dispatcher.TryEnqueue(() => { lines.AppendLine(line); log.Text = lines.ToString(); }),
            e =>
            {
                if (e.Type == "cell") cells.Enqueue(e);
                if (e.Type == "step" && e.Text("state") == "end")
                    dispatcher.TryEnqueue(() => steps.Children.Add(Ui.Mark(e.Number("code") == 0, e.Text("name") ?? "")));
            });
        busy = false;
        ring.IsActive = false;
        RefreshButtons();
        if (!Packaging.IsPackaged) install.Content = Text.Get("Update");
        ShowResults(code, cells.ToList(), chosen, lines.ToString());
    }

    /// <summary>The ✓ / ✗ table per product and AI app, the phone guide, and the full output.</summary>
    public void ShowResults(int code, IReadOnlyList<PackEvent> cells, IReadOnlyList<string> chosen, string output)
    {
        foreach (var old in results.Children.OfType<Expander>()) old.Content = null;
        results.Children.Clear();
        log.Text = output;
        results.Children.Add(Ui.Subtitle(Text.Get("Result")));
        results.Children.Add(Ui.Body(Text.Get(code == 0 ? "Done" : "Failed")));
        var table = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        var ais = cells.Select(c => c.Text("ai")!).Distinct().ToList();
        var products = cells.Select(c => c.Text("product")!).Distinct().ToList();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in ais) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r <= products.Count; r++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        static string Column(string key) => key == "pc" ? Products.Keypad.Device! : AiApps.Name(AiApps.All.First(a => AiApps.Key(a) == key));
        for (var c = 0; c < ais.Count; c++) Place(table, Ui.Strong(Column(ais[c])), 0, c + 1);
        for (var r = 0; r < products.Count; r++)
        {
            Place(table, Ui.Strong(Products.ByKey(products[r]).Name), r + 1, 0);
            for (var c = 0; c < ais.Count; c++)
            {
                var cell = cells.FirstOrDefault(x => x.Text("product") == products[r] && x.Text("ai") == ais[c]);
                if (cell is null) continue;
                var state = cell.Text("state");
                var label = cell.Flag("ok") ? "ok" : state is "disabled" or "refused" or "invalid" ? Text.Get("Cell." + state) : "✗";
                if (!cell.Flag("ok") && state is "disabled" or "refused") Place(table, Ui.Body("– " + label, secondary: true), r + 1, c + 1);
                else Place(table, Ui.Mark(cell.Flag("ok"), label), r + 1, c + 1);
            }
        }
        results.Children.Add(Ui.Card(table));
        foreach (var product in Products.All.Where(p => chosen.Contains(p.Key)))
            foreach (var step in Steps.For(product, folders, found)) results.Children.Add(step);
        results.Children.Add(new Expander { Header = Text.Get("Details"), Content = log, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        results.UpdateLayout();
        results.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
    }

    static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
