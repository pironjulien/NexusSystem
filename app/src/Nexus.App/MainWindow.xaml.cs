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
        AppWindow.Resize(new SizeInt32(1294, 800));  // 800 × the golden ratio
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "nexus.ico"));
        TitleIcon.Source = Ui.LogoSource("hub");

        BuildMenu("home");
        // The online catalog may add, move or release a product: the menu and the page follow it at once.
        _ = RefreshCatalogAsync();
    }

    async Task RefreshCatalogAsync()
    {
        try
        {
            if (await Catalog.RefreshCatalogAsync())
                BuildMenu(Nav.SelectedItem is NavigationViewItem { Tag: string tag } && (Catalog.Shown.ByKey(tag) is not null || tag == "updates") ? tag : "home");
        }
        catch (Exception error) { AppLog.Error("catalog", error); }
    }

    void BuildMenu(string selected)
    {
        Nav.MenuItems.Clear();
        Nav.MenuItems.Add(Item(Text.Get("Home"), "home", Ui.LogoSource("hub")));
        Nav.FooterMenuItems.Clear();
        Nav.FooterMenuItems.Add(new NavigationViewItem { Content = Text.Get("Updates.Title"), Tag = "updates", Icon = new FontIcon { Glyph = "\uE896" } });
        foreach (var group in Catalog.Shown.Groups)
        {
            Nav.MenuItems.Add(new NavigationViewItemHeader { Content = Catalog.Group(group.Key) });
            foreach (var product in Catalog.Shown.Products.Where(p => p.Group == group.Key))
                Nav.MenuItems.Add(Item(product.Name, product.Key, Ui.LogoSource(product)));
        }
        Nav.SelectedItem = MenuItem(selected);
    }

    static NavigationViewItem Item(string text, string tag, ImageSource logo) => new()
    {
        Content = text,
        Tag = tag,
        Icon = new ImageIcon { Source = logo },
    };

    void OnNavigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }) Show(tag);
    }

    void Show(string tag)
    {
        // Pages are built in code: shown directly (a Frame only navigates to pages known to the XAML compiler).
        Body.Content = tag switch { "home" => new HomePage(), "updates" => new UpdatesPage(), _ => new ProductPage(Catalog.Shown.ByKey(tag)!) };
    }

    /// <summary>Called by the home page: show a product's page.</summary>
    public void Open(Product product) => Nav.SelectedItem = MenuItem(product.Key);

    NavigationViewItem MenuItem(string tag) =>
        Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);

    /// <summary>
    /// --capture: pictures of the pages, for the milestone reports and the Store listing drafts. The window is shown
    /// without activation, away from the visible desktop, so the user's foreground and focus are never taken.
    /// </summary>
    public async Task CaptureAsync(string folder, SizeInt32 size)
    {
        Directory.CreateDirectory(folder);
        AppWindow.ResizeClient(size);  // the Store's desktop screenshots: 1366 x 768 or larger; videos: 1920 x 1080
        ShowQuietly();
        // Mica is not in a rendered picture: its plain fallback color for this theme instead.
        Root.Background = new SolidColorBrush(Root.ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32));
        var lang = Text.French ? "fr" : "en";
        var theme = Root.ActualTheme == ElementTheme.Light ? "light" : "dark";
        foreach (var tag in Catalog.Shown.Products.Select(product => product.Key).Prepend("home").Append("updates"))
        {
            Nav.SelectedItem = MenuItem(tag);
            await Task.Delay(4000);  // pages read the PC's state and the site in the background
            await SaveAsync(Path.Combine(folder, $"{tag}-{lang}-{theme}.png"));
            if (Body.Content is ProductPage page && page.ShowHere())
            {
                await Task.Delay(4000);  // the product's own checks (phone, Gmail, Chrome) answer in the background
                await SaveAsync(Path.Combine(folder, $"{tag}-here-{lang}-{theme}.png"));
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
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var scale = Root.XamlRoot.RasterizationScale;
        // A web page (a product's site page) is drawn by its own browser, never by RenderTargetBitmap: its own
        // picture goes in its place.
        foreach (var web in Descendants(Root).OfType<WebView2>().Where(web => Shown(web) && web.CoreWebView2 is not null && web.ActualWidth > 0))
        {
            using var preview = new InMemoryRandomAccessStream();
            await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, preview);
            var decoder = await BitmapDecoder.CreateAsync(preview);
            var at = web.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
            var (left, top) = ((int)(at.X * scale), (int)(at.Y * scale));
            var (width, height) = ((uint)Math.Min(web.ActualWidth * scale, bitmap.PixelWidth - left), (uint)Math.Min(web.ActualHeight * scale, bitmap.PixelHeight - top));
            var page = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = width, ScaledHeight = height }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            for (var row = 0; row < height; row++)
                Array.Copy(page, row * width * 4, pixels, ((top + row) * bitmap.PixelWidth + left) * 4, width * 4);
        }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var dpi = (float)(96 * scale);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        await using var output = File.Create(file);
        await stream.AsStreamForRead().CopyToAsync(output);
    }

    // Shown on screen: neither it nor any parent is collapsed (a hidden tab keeps its last size).
    static bool Shown(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
