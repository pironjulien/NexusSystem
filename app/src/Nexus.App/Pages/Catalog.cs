using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexus.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Nexus.App.Pages;

/// <summary>What the app shows of a product on this PC: installed or not, its version, the newest published one.</summary>
sealed record ProductView(Product Product, bool Installed, string? Version, string? Latest, Unfit? Unfit, bool Pending = false)
{
    public bool UpdateAvailable => Installed && Version is not null && Latest is not null && ProductState.CompareVersions(Latest, Version) > 0;
}

/// <summary>
/// The shared pieces of the home and product pages: each product's state, read on this PC and from GitHub (its latest
/// release), and its one way in: the user's AI app (a prompt to paste) or a store.
/// </summary>
static class Catalog
{
    static readonly Dictionary<string, string> Latest = [];
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The products shown: the last online catalog read on this PC, or the one built into the app.</summary>
    public static ProductCatalogData Shown { get; private set; } = ProductCatalog.Load(UserFolders.FromEnvironment());

    /// <summary>Reads the online catalog; true when what the app shows changed.</summary>
    public static async Task<bool> RefreshCatalogAsync(CancellationToken cancel = default)
    {
        if (await ProductCatalog.FetchAsync(Http, UserFolders.FromEnvironment(), cancel) is not { } catalog) return false;
        var changed = !catalog.Groups.SequenceEqual(Shown.Groups) || !catalog.Products.SequenceEqual(Shown.Products, ProductComparer);
        Shown = catalog;
        return changed;
    }

    // Records compare their lists by reference: two reads of the same catalog are equal when every shown fact is.
    static readonly IEqualityComparer<Product> ProductComparer = EqualityComparer<Product>.Create(
        (a, b) => a!.Key == b!.Key && a.Name == b.Name && a.Accent == b.Accent && a.Availability == b.Availability && a.Group == b.Group &&
            a.Page == b.Page && a.StoreUrl == b.StoreUrl && a.Icon == b.Icon && a.Banner == b.Banner && a.Video == b.Video && a.AboutFr == b.AboutFr && a.AboutEn == b.AboutEn,
        p => p.Key.GetHashCode());

    public static string Group(string key) => Shown.Groups.FirstOrDefault(g => g.Key == key) is { } group ? (Text.French ? group.Fr : group.En) : key;

    public static string About(Product product) => (Text.French ? product.AboutFr : product.AboutEn)
        ?? Text.Get(char.ToUpperInvariant(product.Key[0]) + product.Key[1..] + ".About");

    public static ProductView Read(Product product, IReadOnlyList<AiApp> found, UserFolders folders)
    {
        var unfit = product.Availability == Availability.Ready ? Products.Why(product, found, folders) : null;
        var installed = product.Availability != Availability.NotPublic && ProductUpdates.IsInstalled(product, found, folders);
        var version = installed ? ProductUpdates.InstalledVersion(product, folders) : null;
        ProductView view;
        lock (Latest) view = new ProductView(product, installed, version, Latest.GetValueOrDefault(product.Key), unfit);
        lock (Handed)
        {
            // Handed to an AI app: "installing" until the product reports itself installed and up to date (or 15 minutes).
            if (!Handed.TryGetValue(product.Key, out var since)) return view;
            if (view.Installed && !view.UpdateAvailable || DateTimeOffset.UtcNow - since > TimeSpan.FromMinutes(15))
            {
                Handed.Remove(product.Key);
                return view;
            }
        }
        return view with { Pending = true };
    }

    static readonly Dictionary<string, DateTimeOffset> Handed = [];

    /// <summary>What the pages compare to redraw a card only when its state changed.</summary>
    public static string Signature(ProductView view) =>
        $"{view.Product.Key}|{view.Installed}|{view.Version}|{view.Latest}|{view.Unfit}|{view.Pending}";

    /// <summary>The newest release of every product on GitHub (no API, no rate limit), in parallel.</summary>
    public static async Task RefreshLatestAsync(UserFolders folders, CancellationToken cancel = default)
    {
        await Task.WhenAll(Shown.Products.Select(async product =>
        {
            if (ReleaseFeed.Repository(product) is not { } repository) return;
            if (await ReleaseFeed.NewestAsync(repository, cancel) is not { } tag) return;
            lock (Latest) Latest[product.Key] = ReleaseFeed.Version(tag);
        }));
    }

    public static bool HasLatest { get { lock (Latest) return Latest.Count > 0; } }

    /// <summary>The state line of a card or a page: a colored dot and a few words.</summary>
    public static Grid State(ProductView view) => view switch
    {
        { Product.Availability: Availability.NotPublic } => Ui.State(Text.Get("NotPublic"), "TextFillColorDisabledBrush"),
        { Pending: true } => Ui.State(Text.Get("State.Pending"), "AccentFillColorDefaultBrush", busy: true),
        { Installed: false, Unfit: { } unfit } => Ui.State(Unfitting(view.Product, unfit), "TextFillColorDisabledBrush"),
        { Installed: false } => Ui.State(Text.Get("State.NotInstalled"), "TextFillColorDisabledBrush"),
        { Product.StoreUrl: not null } => Ui.State(Text.Get("State.Store"), "SystemFillColorSuccessBrush"),
        { UpdateAvailable: true } => Ui.State(Text.Format("State.Update", view.Latest!), "SystemFillColorCautionBrush"),
        { Version: { } version, Latest: not null } => Ui.State(Text.Format("State.UpToDate", version), "SystemFillColorSuccessBrush"),
        { Version: { } version } => Ui.State(Text.Format("State.InstalledVersion", version), "SystemFillColorSuccessBrush"),
        _ => Ui.State(Text.Get("State.Installed"), "SystemFillColorSuccessBrush"),
    };

    static string Unfitting(Product product, Unfit unfit) => unfit switch
    {
        Unfit.NeedsDevice => Text.Get("NeedsLogi"),
        Unfit.NeedsApp => Text.Get("NeedsClaudeCode"),
        Unfit.NeedsTwo => Text.Get("NeedsTwo"),
        _ => Text.Format("OnlyFor", string.Join(", ", product.Targets.Select(AiApps.Name))),
    };

    /// <summary>
    /// The product's one way in: its store, or the user's AI app (the prompt is copied, to paste in Claude Code, Codex
    /// or Antigravity). None when it is not public, does not fit this PC, or is installed and up to date.
    /// </summary>
    public static Button? Action(ProductView view, IReadOnlyList<AiApp> found, Action<string> said, bool large = false)
    {
        var button = ActionButton(view, found, said);
        // The product page's one button, as large as a store's "Get" button.
        if (button is not null && large)
        {
            button.MinWidth = 220;
            button.Padding = new Thickness(24, 10, 24, 10);
            button.FontSize = 15;
        }
        return button;
    }

    static Button? ActionButton(ProductView view, IReadOnlyList<AiApp> found, Action<string> said)
    {
        var product = view.Product;
        if (product.StoreUrl is { } store)
        {
            var open = new Button { Content = Text.Get(view.Installed ? "OpenStore" : "GetStore") };
            if (!view.Installed) open.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            open.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(store));
            return open;
        }
        if (product.Availability != Availability.Ready || view.Unfit is not null || view.Pending || view.Installed && !view.UpdateAvailable) return null;
        var install = new Button
        {
            Content = Text.Get(view.Installed ? "UpdateAi" : "InstallAi"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        HandOff(install, () => [product.Key], found, said);
        return install;
    }

    /// <summary>
    /// The button hands the install to an AI app: Claude Code and Codex open on a new conversation with the request
    /// already written (their own links, claude://code/new and codex://threads/new: the user only presses Enter);
    /// Antigravity has no such link: it is opened and the request written in its chat box (AntigravityDraft), or
    /// copied to paste when that fails. With several AI apps
    /// on the PC, the button opens a menu to pick one.
    /// </summary>
    public static void HandOff(Button button, Func<IReadOnlyList<string>> keys, IReadOnlyList<AiApp> found, Action<string> said)
    {
        if (found.Count == 1)
        {
            button.Click += async (_, _) => said(await HandAsync(keys(), found[0]));
            return;
        }
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var app in found)
        {
            var item = new MenuFlyoutItem { Text = Text.Format("HandTo", AiApps.Name(app)), Icon = new ImageIcon { Source = Ui.AiLogoSource(app) } };
            item.Click += async (_, _) => said(await HandAsync(keys(), app));
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        var copy = new MenuFlyoutItem { Text = Text.Get("CopyPromptOnly"), Icon = new FontIcon { Glyph = "" } };
        copy.Click += (_, _) => said(Copy(keys()) is null ? Text.Get("PromptFailed") : Text.Get("PromptCopied"));
        menu.Items.Add(copy);
        button.Flyout = menu;
    }

    static async Task<string> HandAsync(IReadOnlyList<string> keys, AiApp app)
    {
        if (Copy(keys) is not { } prompt) return Text.Get("PromptFailed");
        lock (Handed) foreach (var key in keys) Handed[key] = DateTimeOffset.UtcNow;
        var link = app switch
        {
            AiApp.ClaudeCode => "claude://code/new?q=" + Uri.EscapeDataString(prompt),
            AiApp.Codex => "codex://threads/new?prompt=" + Uri.EscapeDataString(prompt),
            _ => null,
        };
        try
        {
            if (link is not null && await Windows.System.Launcher.LaunchUriAsync(new Uri(link))) return Text.Format("HandOpened", AiApps.Name(app));
            var antigravity = Path.Combine(UserFolders.FromEnvironment().LocalAppData, "Programs", "Antigravity", "Antigravity.exe");
            if (app == AiApp.Antigravity && File.Exists(antigravity))
            {
                // Already open: Antigravity comes forward in its own window instead of starting again. Its chat box is
                // then filled (never sent); the copied request stays the way back when that fails.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(antigravity) { UseShellExecute = true })?.Dispose();
                var written = await AntigravityDraft.WriteAsync(prompt, UserFolders.FromEnvironment(), Http, TimeSpan.FromSeconds(45));
                return Text.Format(written ? "HandOpened" : "HandPaste", AiApps.Name(app));
            }
        }
        catch (Exception error) { AppLog.Error("handoff", error); }
        return Text.Format("HandCopied", AiApps.Name(app));
    }

    /// <summary>Copies the install request of these products (also the fallback of every hand-off); null when it fails.</summary>
    static string? Copy(IReadOnlyList<string> keys)
    {
        var prompt = Products.AiPrompt(keys, Text.French, Shown.Products);
        if (prompt.Length == 0) return null;
        try
        {
            var package = new DataPackage();
            package.SetText(prompt);
            Clipboard.SetContent(package);
            return prompt;
        }
        catch (Exception error)
        {
            AppLog.Error("prompt", error);
            return null;
        }
    }
}
