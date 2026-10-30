using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Nexus.App.Pages;
using Nexus.Core;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Nexus.App;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        Title = "NexusSystem";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new SizeInt32(1180, 800));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "nexus.ico"));
        TitleIcon.Source = Ui.LogoSource("hub");

        Nav.MenuItems.Add(Item(Text.Get("Home"), "home", Ui.LogoSource("hub")));
        Nav.MenuItems.Add(new NavigationViewItemHeader { Content = Text.Get("Products") });
        foreach (var product in Products.All)
        {
            Nav.MenuItems.Add(Item(product.Name, product.Key, Ui.LogoSource(product.Key)));
        }
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    static NavigationViewItem Item(string text, string tag, ImageSource logo) => new()
    {
        Content = text,
        Tag = tag,
        Icon = new ImageIcon { Source = logo },
    };

    void OnNavigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args) => Show((string)((NavigationViewItem)args.SelectedItem).Tag);

    void Show(string tag)
    {
        // Pages are built in code: shown directly (a Frame only navigates to pages known to the XAML compiler).
        Body.Content = tag == "home" ? new HomePage() : new ProductPage(Products.ByKey(tag));
    }

    /// <summary>Called by the home page: show a product's page.</summary>
    public void Open(Product product) =>
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == product.Key);

    /// <summary>
    /// --capture: pictures of the pages, for the milestone reports and the Store listing drafts. The window is shown
    /// without activation, away from the visible desktop, so the user's foreground and focus are never taken.
    /// </summary>
    public async Task CaptureAsync(string folder, SizeInt32 size, bool frames = false)
    {
        Directory.CreateDirectory(folder);
        AppWindow.ResizeClient(size);  // the Store's desktop screenshots: 1366 x 768 or larger; videos: 1920 x 1080
        ShowQuietly();
        // Mica is not in a rendered picture: its plain fallback color for this theme instead.
        Root.Background = new SolidColorBrush(Root.ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32));
        var lang = Text.French ? "fr" : "en";
        var theme = Root.ActualTheme == ElementTheme.Light ? "light" : "dark";
        foreach (var tag in new[] { "home", "windows", "android", "browser", "keypad", "relay", "agymail" })
        {
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
            await Task.Delay(2500);  // pages read the PC's state in the background
            await SaveAsync(Path.Combine(folder, $"{tag}-{lang}-{theme}.png"));
            if (tag == "home" && Body.Content is HomePage home)
            {
                if (frames)
                {
                    // Video frames: products unticked then ticked again, then the screen while the pack runs.
                    var n = 0;
                    async Task Frame() => await SaveAsync(Path.Combine(folder, $"frame-{lang}-{theme}-{++n:00}.png"));
                    await Frame();
                    foreach (var (key, on) in new[] { ("android", false), ("keypad", false), ("agymail", false), ("android", true), ("keypad", true), ("agymail", true) })
                    {
                        home.Pick(key, on);
                        await Task.Delay(400);
                        await Frame();
                    }
                    home.ShowInstalling();
                    await Task.Delay(800);
                    await Frame();
                }
                // The end of an install from the Store app, as the pack reports it: every cell green, Claude Desktop
                // left to the extensions (the pack skips it there).
                var cells = new List<PackEvent>();
                foreach (var product in new[] { "windows", "android", "browser", "relay", "agymail" })
                    foreach (var ai in Products.ByKey(product).Targets)
                        cells.Add(PackRunner.ParseEvent($"NEXUS_PACK_EVENT {{\"type\":\"cell\",\"product\":\"{product}\",\"ai\":\"{AiApps.Key(ai)}\",\"ok\":true}}")!);
                cells.Add(PackRunner.ParseEvent("NEXUS_PACK_EVENT {\"type\":\"cell\",\"product\":\"keypad\",\"ai\":\"pc\",\"ok\":true}")!);
                home.ShowResults(0, cells, ["windows", "android", "browser", "relay", "agymail", "keypad"], "");
                await Task.Delay(1500);
                await SaveAsync(Path.Combine(folder, $"home-result-{lang}-{theme}.png"));
            }
        }
        Application.Current.Exit();
    }

    /// <summary>Shown away from the visible desktop, never activated, without a taskbar button.</summary>
    public void ShowQuietly()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLongPtrW(hwnd, GwlExStyle, GetWindowLongPtrW(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        AppWindow.Move(new PointInt32(-4000, -4000));
        AppWindow.Show(activateWindow: false);
    }

    const int GwlExStyle = -20;
    const nint WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    async Task SaveAsync(string file)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var dpi = (float)(96 * Root.XamlRoot.RasterizationScale);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels.ToArray());
        await encoder.FlushAsync();
        stream.Seek(0);
        await using var output = File.Create(file);
        await stream.AsStreamForRead().CopyToAsync(output);
    }
}
