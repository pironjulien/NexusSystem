using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Windows.Management.Deployment;

namespace Nexus.Core;

/// <summary>
/// A Microsoft Store product (NexusWpp): the version Windows has installed, the version the Store serves and its update.
/// The served version comes from the Store's display catalog (its MSIX package name): winget's msstore source reports
/// "Unknown" for an MSIX app, so `winget upgrade` never sees a new version, and `winget install --force` takes it.
/// </summary>
public static class StoreApps
{
    /// <summary>The Store product id of its listing link (ms-windows-store://pdp/?productid=9N2VCVDH7QDX), or null.</summary>
    public static string? ProductId(Product product)
    {
        const string key = "productid=";
        if (product.StoreUrl is not { } url || url.IndexOf(key, StringComparison.OrdinalIgnoreCase) is var at && at < 0) return null;
        var id = url[(at + key.Length)..].Split('&')[0];
        return id.Length > 0 && id.All(char.IsAsciiLetterOrDigit) ? id : null;
    }

    /// <summary>The version of its package installed for this user (PackageManager, no elevation), or null.</summary>
    public static string? InstalledVersion(Product product)
    {
        if (product.PackageFamily is not { } family) return null;
        try
        {
            return new PackageManager().FindPackagesForUser("", family)
                .Select(package => package.Id.Version)
                .Select(v => $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}")
                .OrderByDescending(version => version, Comparer<string>.Create(ProductState.CompareVersions))
                .FirstOrDefault();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The version the Store serves in this PC's region, or null when it does not answer.</summary>
    public static async Task<string?> PublishedAsync(Product product, HttpClient http, CancellationToken cancel = default)
    {
        if (ProductId(product) is not { } id || product.PackageFamily is not { } family) return null;
        var market = RegionInfo.CurrentRegion.TwoLetterISORegionName is { Length: 2 } region ? region : "US";
        try
        {
            var json = await http.GetStringAsync(new Uri($"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={id}&market={market}&languages=en-US"), cancel);
            return PublishedVersion(json, family);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The highest version among the catalog's packages of this family (Name_Version_Arch_Resource_Publisher).</summary>
    public static string? PublishedVersion(string json, string family)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var versions = new List<string>();
            foreach (var product in Array(document.RootElement, "Products"))
                foreach (var availability in Array(product, "DisplaySkuAvailabilities"))
                    if (availability.TryGetProperty("Sku", out var sku) && sku.TryGetProperty("Properties", out var properties))
                        foreach (var package in Array(properties, "Packages"))
                            if (package.TryGetProperty("PackageFullName", out var name) && name.GetString()?.Split('_') is [var app, var version, _, _, var publisher]
                                && string.Equals($"{app}_{publisher}", family, StringComparison.OrdinalIgnoreCase))
                                versions.Add(version);
            return versions.OrderByDescending(version => version, Comparer<string>.Create(ProductState.CompareVersions)).FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }

        static IEnumerable<JsonElement> Array(JsonElement parent, string key) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray() : [];
    }

    /// <summary>The winget arguments that take the Store's current package even when `winget upgrade` sees none.</summary>
    public static string UpdateArguments(string productId) =>
        $"install --id {productId} --source msstore --force --silent --disable-interactivity --accept-package-agreements --accept-source-agreements";

    /// <summary>Installs the Store's current package through winget, without a window; true when winget succeeded.</summary>
    public static async Task<bool> UpdateAsync(Product product, CancellationToken cancel = default)
    {
        if (ProductId(product) is not { } id) return false;
        var start = new ProcessStartInfo("winget.exe", UpdateArguments(id))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null) return false;
            // Read both streams so a full pipe never blocks winget.
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var errors = process.StandardError.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            await Task.WhenAll(output, errors);
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
