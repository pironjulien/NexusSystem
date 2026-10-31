using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Nexus.Core;
using Windows.UI;

namespace Nexus.App.Pages;

/// <summary>Small builders for Fluent cards and text, so the pages read as a list of what they show.</summary>
static class Ui
{
    public static Brush Theme(string key) => (Brush)Application.Current.Resources[key];

    public static SolidColorBrush Accent(string hex) => new(Color.FromArgb(0xFF,
        Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));

    /// <summary>The product color for text: as on the site in dark theme, darkened in light theme to stay readable.</summary>
    public static SolidColorBrush AccentText(string hex)
    {
        var color = Accent(hex).Color;
        if (Application.Current.RequestedTheme != ApplicationTheme.Light) return new SolidColorBrush(color);
        static byte Dark(byte v) => (byte)(v * 0.55);
        return new SolidColorBrush(Color.FromArgb(0xFF, Dark(color.R), Dark(color.G), Dark(color.B)));
    }

    public static Border Card(UIElement content, string? accent = null, double padding = 16) => new()
    {
        Child = content,
        Padding = new Thickness(padding),
        CornerRadius = new CornerRadius(8),
        Background = Theme("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = accent is null ? Theme("CardStrokeColorDefaultBrush") : Accent(accent),
        BorderThickness = new Thickness(accent is null ? 1 : 1.5),
    };

    public static TextBlock Title(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };

    public static TextBlock Subtitle(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };

    public static TextBlock Body(string text, bool secondary = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.WrapWholeWords,
        Foreground = secondary ? Theme("TextFillColorSecondaryBrush") : Theme("TextFillColorPrimaryBrush"),
    };

    public static TextBlock Strong(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold };

    /// <summary>
    /// The icons of the Nexus family kit, as on julienpiron.fr ("nexus" + key; the hub is NexusSystem, the cyan chip
    /// with the π): from 44 px the mark (the pictogram in the site's medallion, nexuswindows-mark.png), under 44 px
    /// the same medallion cut round (nexuswindows-round.png, transparent corners); never the pictogram alone.
    /// </summary>
    public static ImageSource LogoSource(string key, double size = 20)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var name = "nexus" + (key == "hub" ? "system" : key);
        return new BitmapImage(new Uri(Path.Combine(assets, name + (size >= 44 ? "-mark.png" : "-round.png"))));
    }

    public static Image Logo(string key, double size) => new() { Width = size, Height = size, Source = LogoSource(key, size) };

    /// <summary>A product's icon: the app's own picture, or for a product the online catalog added, its icon on the site.</summary>
    public static ImageSource LogoSource(Product product, double size = 20) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "nexus" + product.Key + "-round.png")) || product.Icon is null
            ? LogoSource(product.Key, size) : new BitmapImage(new Uri(product.Icon));

    public static Image Logo(Product product, double size) => new() { Width = size, Height = size, Source = LogoSource(product, size) };

    /// <summary>Official logo of an AI app (Claude, Codex, Antigravity) from Assets/ai-*.png.</summary>
    public static ImageSource AiLogoSource(AiApp app)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var name = app switch
        {
            AiApp.ClaudeCode => "ai-claude.png",
            AiApp.Codex => "ai-codex.png",
            _ => "ai-antigravity.png",
        };
        return new BitmapImage(new Uri(Path.Combine(assets, name)));
    }

    public static Image AiLogo(AiApp app, double size = 18) => new() { Width = size, Height = size, Source = AiLogoSource(app) };

    /// <summary>Pill badge showing an AI app's official logo, name, and optional ✓ check mark.</summary>
    public static Border AiPill(AiApp app, bool? declared = null, bool compact = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = compact ? 5 : 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(AiLogo(app, compact ? 14 : 18));
        row.Children.Add(new TextBlock
        {
            Text = AiApps.Name(app),
            FontSize = compact ? 11.5 : 13,
            FontWeight = compact ? FontWeights.Normal : FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (declared is { } ok)
        {
            row.Children.Add(new FontIcon
            {
                Glyph = ok ? "" : "",
                FontSize = compact ? 10 : 12,
                Foreground = ok ? Theme("SystemFillColorSuccessBrush") : Theme("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return new Border
        {
            Child = row,
            Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(compact ? 10 : 14),
            Background = Theme("SubtleFillColorSecondaryBrush"),
            BorderBrush = Theme("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>A ✓ / ✗ line: green or red mark, then the text.</summary>
    public static StackPanel Mark(bool ok, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new FontIcon
            {
                Glyph = ok ? "" : "",
                FontSize = 14,
                Foreground = ok ? Theme("SystemFillColorSuccessBrush") : Theme("SystemFillColorCriticalBrush"),
            },
            new TextBlock { Text = text },
        },
    };

    /// <summary>Logo, then title and text that wrap in the remaining width.</summary>
    public static Grid Header(UIElement logo, string title, string text)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(logo);
        var words = Column(4, Title(title), Body(text, secondary: true));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return grid;
    }

    /// <summary>Short lines in a scrolling list (journal), compact like a log.</summary>
    public static ScrollViewer Lines(IEnumerable<string> lines, double height)
    {
        var panel = new StackPanel { Spacing = 2 };
        foreach (var line in lines)
            panel.Children.Add(new TextBlock { Text = line, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, IsTextSelectionEnabled = true });
        return new ScrollViewer { Content = panel, MaxHeight = height };
    }

    public static StackPanel Column(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>A scrolling page that uses the whole width of the window.</summary>
    public static ScrollViewer Page(UIElement content) => new()
    {
        Content = new Border { Child = content, Padding = new Thickness(32, 16, 32, 24), HorizontalAlignment = HorizontalAlignment.Stretch },
    };

    /// <summary>
    /// Cards in as many columns as the width allows (at least <paramref name="minWidth"/> each), like the Microsoft
    /// Store: the grid reflows when the window is resized.
    /// </summary>
    public static Grid Flow(IReadOnlyList<FrameworkElement> items, double minWidth = 340, double spacing = 12)
    {
        var grid = new Grid { ColumnSpacing = spacing, RowSpacing = spacing };
        var columns = 0;
        void Layout(double width)
        {
            var count = Math.Max(1, (int)((width + spacing) / (minWidth + spacing)));
            if (count == columns) return;
            columns = count;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var c = 0; c < count; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < (items.Count + count - 1) / count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < items.Count; i++)
            {
                Grid.SetColumn(items[i], i % count);
                Grid.SetRow(items[i], i / count);
            }
        }
        foreach (var item in items) grid.Children.Add(item);
        Layout(minWidth * 2);
        grid.SizeChanged += (_, e) => Layout(e.NewSize.Width);
        return grid;
    }

    /// <summary>A colored dot then a short state, as on a store listing ("Installed", "Update available").</summary>
    public static Grid State(string text, string brush)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = Theme(brush), VerticalAlignment = VerticalAlignment.Center });
        var words = new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.WrapWholeWords, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme("TextFillColorSecondaryBrush") };
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return grid;
    }

    public static Color Transparent => Colors.Transparent;
}
