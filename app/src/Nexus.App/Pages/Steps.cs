using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexus.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Nexus.App.Pages;

/// <summary>
/// What is left to the user once a product is installed, the same steps as the end of get-nexus.ps1, with a button
/// where the app can do the next click for them (open Chrome on its extensions page, pair it, copy a folder path).
/// </summary>
static class Steps
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// The steps of a product on this PC: to do (shown only while there is something left to do, checked on this PC),
    /// or help (guides kept one click away, never shown as if something were missing).
    /// </summary>
    public static IEnumerable<(UIElement View, bool Help)> For(Product product, UserFolders folders, IReadOnlyList<AiApp> ais)
    {
        if (product.Key == "browser")
        {
            // Only while Chrome is open without the extension connected: closed or connected, nothing is left to do.
            var extension = Extension(product, folders);
            yield return (extension, false);
            yield return (Pairing(product, folders, extension), false);
        }
        // Claude Desktop (its chat): the product's extension (.mcpb) of its latest release, which the user drops on Claude
        // Desktop; not offered when this PC's settings skip Claude Desktop or the product (~\.nexus\nexussystem.json).
        if (DesktopExtension.For(product) is { } desktop && desktop.Offered(folders, LocalSettings.Read(folders)))
            yield return (Desktop(product, desktop, folders), true);
        switch (product.Key)
        {
            case "android":
                yield return (Card(product, Text.Get("Phone"), Text.Get("PhoneSteps")), true);
                break;
            // The Codex project, only when Codex answers (Claude Code is the host) and no execution space is chosen yet.
            case "relay" when ais.Contains(AiApp.ClaudeCode) && ais.Contains(AiApp.Codex) && !RelaySpaceChosen(folders):
                yield return (Card(product, Text.Get("Relay.Step"), Text.Format("Relay.StepText", RelayProject(folders)),
                    Action(Text.Get("CopyPath"), () => Copy(RelayProject(folders)))), false);
                break;
            case "agymail":
                yield return (Card(product, Text.Get("Agymail.Step"), Text.Get("Agymail.StepText"),
                    Link(Text.Get("Guide"), "https://github.com/pironjulien/NexusAGYmail#quick-start")), true);
                break;
            // The keys only while they are not on the pad yet (an install placed them, or the user did).
            case "keypad" when Declarations.DeviceInstalled(product, folders) && !Declarations.KeypadKeysPlaced(folders):
                yield return (Card(product, Text.Get("Keypad.Step"), Text.Get("Keypad.StepText"),
                    Link(Text.Get("Guide"), "https://github.com/pironjulien/NexusKeypad#install")), false);
                break;
        }
    }

    static string RelayProject(UserFolders folders) => Path.Combine(folders.Home, ".nexus", "relay", "codex-projet");

    // Relay's explicit Codex execution space, complete as NexusRelay's CodexSpace requires (workspace, label, project id).
    static bool RelaySpaceChosen(UserFolders folders)
    {
        try
        {
            using var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folders.Home, ".nexus", "nexus-relay", "settings.json")));
            return new[] { "codex_workspace", "codex_project", "codex_project_id" }.All(key =>
                settings.RootElement.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    static Border Card(Product product, string title, string text, params UIElement[] actions)
    {
        var column = Ui.Column(8, Ui.Strong(title), Ui.Body(text));
        if (actions.Length > 0) column.Children.Add(Row(actions));
        return Ui.Card(column, product.Accent);
    }

    static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    static Button Action(string text, Action run)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) =>
        {
            try
            {
                run();
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                AppLog.Error("step", error);
            }
        };
        return button;
    }

    static HyperlinkButton Link(string text, string url) => new() { Content = text, NavigateUri = new Uri(url), Padding = new Thickness(0) };

    static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // A secret (the NexusBrowser pairing code) stays out of the clipboard history (Win+V), the cloud clipboard and
    // clipboard monitors: IsAllowedInHistory and IsRoamable write Windows' CanIncludeInClipboardHistory and
    // CanUploadToCloudClipboard formats (DWORD 0); ExcludeClipboardContentFromMonitorProcessing is added by hand.
    static void CopySecret(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        package.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]).AsRandomAccessStream());
        Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false });
    }

    // The extension paired with the bridge without the native host (NexusBrowser 2.25.0): the state read from the
    // bridge, "Pair Chrome" opens the extension's pairing page in the running Chrome, "Show a code" for the popup.
    // A bridge without pairing gets the guided fallback: the official PowerShell line, run by the user.
    static Border Pairing(Product product, UserFolders folders, Border extension)
    {
        var state = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords };
        var note = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords, IsTextSelectionEnabled = true, Visibility = Visibility.Collapsed };
        var pair = new Button { Content = Text.Get("Pair.Button"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var code = new Button { Content = Text.Get("Pair.Code") };
        var actions = Row(pair, code);
        var column = Ui.Column(8, Ui.Strong(Text.Get("Pair.Step")), Ui.Body(Text.Get("Pair.StepText")), state, actions, note);
        var card = Ui.Card(column, product.Accent);

        void Show(string text)
        {
            note.Text = text;
            note.Visibility = Visibility.Visible;
        }

        async Task Refresh()
        {
            var bridge = await BrowserBridge.StateAsync(folders, Http);
            state.Text = Text.Get("Pair." + bridge);
            // Both steps only while Chrome runs without the extension connected; a note being read stays in sight.
            var todo = bridge == BridgeState.Waiting || note.Visibility == Visibility.Visible && bridge != BridgeState.Connected;
            card.Visibility = extension.Visibility = todo ? Visibility.Visible : Visibility.Collapsed;
            actions.Visibility = bridge == BridgeState.Waiting ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task Offer(bool page)
        {
            try
            {
                var secret = await BrowserBridge.OfferAsync(folders, Http);
                if (secret is null)
                {
                    Copy(InstallPage);
                    Show(Text.Format("Pair.Fallback", InstallPage));
                    return;
                }
                if (!page)
                {
                    CopySecret(secret);
                    Show(Text.Get("Pair.CodeCopied"));
                    return;
                }
                if (BrowserBridge.ChromePath() is null)
                {
                    Show(Text.Get("Browser.NoChrome"));
                    return;
                }
                if (!BrowserBridge.ChromeRunning() || BrowserBridge.ChromePath() is not { } chrome || BrowserBridge.ExtensionId(folders) is not { } id)
                {
                    Show(Text.Get("Pair.OpenChrome"));
                    return;
                }
                BrowserBridge.OpenPairingPage(chrome, id, secret);
                Show(Text.Get("Pair.Opened"));
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                AppLog.Error("pair", error);
                Show(Text.Get("Pair.Failed"));
            }
            await Refresh();
        }

        pair.Click += async (_, _) => await Offer(page: true);
        code.Click += async (_, _) => await Offer(page: false);
        card.Visibility = extension.Visibility = Visibility.Collapsed;
        // Follows Chrome while the page is open: opened, paired, closed.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) => await Refresh();
        card.Loaded += (_, _) => timer.Start();
        card.Unloaded += (_, _) => timer.Stop();
        _ = Refresh();
        return card;
    }

    // Its install section: the get.ps1 file of its release, checked, run with -File (never downloaded code in memory).
    const string InstallPage = "https://github.com/pironjulien/NexusBrowser#install";

    // The extension step: the button says what it did, or why it could not (Chrome missing: its official download
    // page offered; NexusBrowser not installed yet). Never a click without an answer.
    static Border Extension(Product product, UserFolders folders)
    {
        var note = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords, Visibility = Visibility.Collapsed };
        var install = new Button { Content = Text.Get("Browser.InstallChrome"), Visibility = Visibility.Collapsed };
        install.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(ChromeDownload));
        var open = new Button { Content = Text.Get("Browser.StepButton") };
        open.Click += (_, _) =>
        {
            install.Visibility = Visibility.Collapsed;
            if (BrowserBridge.ChromePath() is null)
            {
                note.Text = Text.Get("Browser.NoChrome");
                install.Visibility = Visibility.Visible;
            }
            else if (GuideScript(folders) is not { } guide)
            {
                note.Text = Text.Get("Browser.NotInstalled");
            }
            else
            {
                try
                {
                    OpenExtensionGuide(guide);
                    note.Text = Text.Get("Browser.Opened");
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    AppLog.Error("step", error);
                    note.Text = Text.Get("Browser.Failed");
                }
            }
            note.Visibility = Visibility.Visible;
        };
        return Ui.Card(Ui.Column(8, Ui.Strong(Text.Get("Browser.Step")), Ui.Body(Text.Get("Browser.StepText")), Row(open, install), note), product.Accent);
    }

    const string ChromeDownload = "https://www.google.com/chrome/";

    // Claude Desktop's extension: downloaded (checked against its release's SHA256SUMS.txt when listed) into
    // ~\.nexus\claude-desktop, then shown in File Explorer; the user drops it on Claude Desktop (Settings > Extensions),
    // which asks for its own confirmation. The button always answers.
    static Border Desktop(Product product, DesktopExtension extension, UserFolders folders)
    {
        var installed = extension.IsInstalled(folders);
        var note = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords, Visibility = Visibility.Collapsed };
        var download = new Button { Content = Text.Get(installed ? "Desktop.Update" : "Desktop.Download") };
        download.Click += async (_, _) =>
        {
            download.IsEnabled = false;
            try
            {
                var file = await extension.DownloadAsync(folders, Http);
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{file}\"") { UseShellExecute = false });
                note.Text = Text.Format("Desktop.Saved", file);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or
                UnauthorizedAccessException or TaskCanceledException or System.ComponentModel.Win32Exception)
            {
                AppLog.Error("desktop-extension", error);
                note.Text = Text.Get("Desktop.Failed");
            }
            finally
            {
                download.IsEnabled = true;
                note.Visibility = Visibility.Visible;
            }
        };
        return Ui.Card(Ui.Column(8, Ui.Strong(Text.Get("Desktop.Step")),
            Ui.Body(Text.Get(installed ? "Desktop.Installed" : "Desktop.StepText")), Row(download), note), product.Accent);
    }

    static string? GuideScript(UserFolders folders)
    {
        var guide = Path.Combine(folders.LocalAppData, "NexusBrowser", "app", "scripts", "guide-extension.ps1");
        return File.Exists(guide) ? guide : null;
    }

    // NexusBrowser's own guide: Chrome opened on chrome://extensions, the extension folder copied, the clicks listed.
    static void OpenExtensionGuide(string guide)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", guide, "-WaitSeconds", "0" }) start.ArgumentList.Add(arg);
        start.Environment.Remove("PSModulePath");
        Process.Start(start);
    }
}
