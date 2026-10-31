using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexus.Core;

namespace Nexus.App.Pages;

/// <summary>
/// "Updates", like the Microsoft Store's "Downloads and updates": every product installed on this PC in one list, its
/// state (up to date, or the new version) and its button, and "Get updates", which reads the published versions again
/// and asks the products that update themselves to do it now. Never closes an AI session.
/// </summary>
public sealed partial class UpdatesPage : UserControl
{
    readonly UserFolders folders = UserFolders.FromEnvironment();
    readonly IReadOnlyList<AiApp> found;
    readonly StackPanel rows = new() { Spacing = 8 };
    readonly InfoBar notice = new() { IsClosable = true, IsOpen = false };
    readonly Button check = new() { Content = Text.Get("CheckUpdates"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly CancellationTokenSource lifetime = new();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    string shown = "";

    public UpdatesPage()
    {
        Unloaded += (_, _) => { timer.Stop(); lifetime.Cancel(); };
        found = AiApps.Find(folders);

        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Title(Text.Get("Updates.Title")));
        check.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(check, 1);
        top.Children.Add(check);
        check.Click += async (_, _) => await CheckAsync();

        Content = Ui.Page(Ui.Column(16, top, notice, Ui.Subtitle(Text.Get("Updates.Installed")), rows));
        Fill();
        timer.Tick += (_, _) => { if (Signatures() != shown) Fill(); };
        timer.Start();
        if (!Catalog.HasLatest) _ = RefreshAsync();
    }

    IEnumerable<ProductView> Installed() =>
        Catalog.Shown.Products.Select(product => Catalog.Read(product, found, folders)).Where(view => view.Installed);

    string Signatures() => string.Join(";", Installed().Select(Catalog.Signature));

    async Task RefreshAsync()
    {
        try
        {
            await Catalog.RefreshLatestAsync(folders, lifetime.Token);
            if (!lifetime.IsCancellationRequested) Fill();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Error("releases", error); }
    }

    /// <summary>One row per installed product, updates first, as in the Store's list.</summary>
    void Fill()
    {
        shown = Signatures();
        rows.Children.Clear();
        var views = Installed().OrderByDescending(view => view.UpdateAvailable || view.Pending).ToList();
        if (views.Count == 0) rows.Children.Add(Ui.Body(Text.Get("Updates.None"), secondary: true));
        foreach (var view in views)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = Ui.Logo(view.Product, 44);
            row.Children.Add(logo);
            var name = Ui.Column(2, Ui.Strong(view.Product.Name), Ui.Body(Catalog.Group(view.Product.Group), secondary: true));
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var state = Catalog.State(view);
            state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            var open = new HyperlinkButton { Content = Text.Get("Updates.Open"), VerticalAlignment = VerticalAlignment.Center };
            var product = view.Product;
            open.Click += (_, _) => MainWindow.Instance?.Open(product);
            UIElement action = Catalog.Action(view, found, Say) is { } button ? button : open;
            ((FrameworkElement)action).VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn((FrameworkElement)action, 3);
            row.Children.Add(action);
            rows.Children.Add(Ui.Card(row, padding: 12));
        }
    }

    void Say(string text) => Say(text, InfoBarSeverity.Success);

    void Say(string text, InfoBarSeverity severity)
    {
        if (text.Length == 0) return;
        notice.Message = text;
        notice.Severity = severity;
        notice.IsOpen = true;
    }

    async Task CheckAsync()
    {
        check.IsEnabled = false;
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
            var late = Installed().Where(view => view.UpdateAvailable).Select(view => view.Product.Name).ToList();
            if (late.Count > 0) Say(Text.Format("Updates.Some", string.Join(", ", late)), InfoBarSeverity.Warning);
            else Say(Text.Get(started ? "Updates.Started" : "Updates.UpToDate"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLog.Error("updates", error);
            Say(Text.Get("Updates.Failed"), InfoBarSeverity.Error);
        }
        finally { check.IsEnabled = true; }
    }
}
