using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexus.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Nexus.App.Pages;

/// <summary>What the app shows of a product on this PC: installed or not, its version, the newest published one.</summary>
sealed record ProductView(Product Product, bool Installed, string? Version, string? Latest, Unfit? Unfit)
{
    public bool UpdateAvailable => Installed && Version is not null && Latest is not null && ProductState.CompareVersions(Latest, Version) > 0;
}

/// <summary>
/// The shared pieces of the home and product pages: each product's state, read on this PC and from GitHub (the newest
/// release of its channel), and its one way in: the user's AI app (a prompt to paste) or a store.
/// </summary>
static class Catalog
{
    static readonly Dictionary<string, string> Latest = [];

    public static string Group(Category category) => Text.Get("Products." + category);

    public static string About(Product product) => Text.Get(char.ToUpperInvariant(product.Key[0]) + product.Key[1..] + ".About");

    public static ProductView Read(Product product, IReadOnlyList<AiApp> found, UserFolders folders)
    {
        var unfit = product.Availability == Availability.Ready ? Products.Why(product, found, folders) : null;
        var installed = product.Availability != Availability.NotPublic && ProductUpdates.IsInstalled(product, found, folders);
        var version = installed ? ProductUpdates.InstalledVersion(product, folders) : null;
        lock (Latest) return new ProductView(product, installed, version, Latest.GetValueOrDefault(product.Key), unfit);
    }

    /// <summary>The newest release of every product on GitHub (no API, no rate limit), in parallel.</summary>
    public static async Task RefreshLatestAsync(UserFolders folders, CancellationToken cancel = default)
    {
        var channel = ReleaseFeed.Channel(folders);
        await Task.WhenAll(Products.All.Select(async product =>
        {
            if (ReleaseFeed.Repository(product) is not { } repository) return;
            if (await ReleaseFeed.NewestAsync(repository, channel, cancel) is not { } tag) return;
            lock (Latest) Latest[product.Key] = ReleaseFeed.Version(tag);
        }));
    }

    public static bool HasLatest { get { lock (Latest) return Latest.Count > 0; } }

    /// <summary>The state line of a card or a page: a colored dot and a few words.</summary>
    public static Grid State(ProductView view) => view switch
    {
        { Product.Availability: Availability.NotPublic } => Ui.State(Text.Get("NotPublic"), "TextFillColorDisabledBrush"),
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
    public static Button? Action(ProductView view, Action<string> said)
    {
        var product = view.Product;
        if (product.StoreUrl is { } store)
        {
            var open = new Button { Content = Text.Get(view.Installed ? "OpenStore" : "GetStore") };
            if (!view.Installed) open.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            open.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(store));
            return open;
        }
        if (product.Availability != Availability.Ready || view.Unfit is not null || view.Installed && !view.UpdateAvailable) return null;
        var install = new Button
        {
            Content = Text.Get(view.Installed ? "UpdateAi" : "InstallAi"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        install.Click += (_, _) => said(CopyPrompt([product.Key]));
        return install;
    }

    /// <summary>Copies the install prompt of these products; returns what to tell the user.</summary>
    public static string CopyPrompt(IReadOnlyList<string> keys)
    {
        var prompt = Products.AiPrompt(keys, Text.French);
        if (prompt.Length == 0) return "";
        try
        {
            var package = new DataPackage();
            package.SetText(prompt);
            Clipboard.SetContent(package);
            return Text.Get("PromptCopied");
        }
        catch (Exception error)
        {
            AppLog.Error("prompt", error);
            return Text.Get("PromptFailed");
        }
    }
}
