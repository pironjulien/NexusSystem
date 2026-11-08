using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>A product the hourly maintenance keeps current: its release repository and the AI apps it may serve.</summary>
public sealed record MaintenanceProduct(Product Product, string Repository, IReadOnlyList<string> Targets)
{
    public string Key => Product.Key;
    public string Name => Product.Name;
    /// <summary>Products whose installer runs alone when a newer release exists (their engine and own connections).</summary>
    public bool InstallerOnly => Product.Device is not null || Key is "android" or "relay" or "agymail" or "indesign" or "free";

    /// <summary>The maintained products, in get-nexus.ps1's order.</summary>
    public static readonly IReadOnlyList<MaintenanceProduct> All =
        new[] { "windows", "android", "browser", "relay", "agymail", "indesign", "keypad", "free" }.Select(key =>
        {
            var product = Products.ByKey(key);
            var targets = product.Targets.Select(AiApps.Key).ToList();
            // Claude Desktop's chat configuration is one more route of the products that write one.
            if (key is "windows" or "android" or "browser") targets.Insert(1, "claude-desktop");
            return new MaintenanceProduct(product, ReleaseFeed.Repository(product)!, targets);
        }).ToList();
}

/// <summary>One AI app's route to a product, as maintenance observed it (written to the status file).</summary>
public sealed class MaintenanceRoute
{
    public required string Key { get; init; }
    public required string State { get; set; }
    /// <summary>catalogue, direct, product, custom, or null when the AI app does not declare the product.</summary>
    public string? Route { get; init; }
    public string? PluginVersion { get; set; }
    public bool ActivationPending { get; set; }
    /// <summary>Whether this route carries the guideLoaded field (an observed route; it is never proven: null).</summary>
    public bool Observed { get; init; }
    public string? Reason { get; set; }
}

/// <summary>One product's row of the status file.</summary>
public sealed class MaintenanceRow
{
    public required string Key { get; init; }
    public string? PublishedVersion { get; set; }
    public string? InstalledVersion { get; set; }
    public List<string> LoadedVersions { get; set; } = [];
    public string State { get; set; } = "pending";
    public bool ActivationPending { get; set; }
    public bool Retryable { get; set; }
    public string Message { get; set; } = "";
    public string? Log { get; set; }
    public List<MaintenanceRoute> Profiles { get; init; } = [];
}

/// <summary>The status file ~\.nexus\status\nexussystem-update.json (schema 1), read by the app and get-nexus.ps1.</summary>
public sealed class MaintenanceReceipt
{
    public string State { get; set; } = "checking";
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextCheckAt { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
    public bool ActivationPending { get; set; }
    public bool Retryable { get; set; }
    public string? Latest { get; set; }
    public string? Catalogue { get; set; }
    public List<MaintenanceRow> Products { get; } = [];
    public string Message { get; set; } = "Checking published releases and installed routes.";

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", 1);
            json.WriteString("state", State);
            json.WriteString("checked_at", CheckedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("nextCheckAt", NextCheckAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteBoolean("activationPending", ActivationPending);
            json.WriteBoolean("retryable", Retryable);
            WriteText(json, "latest", Latest);
            WriteText(json, "catalogue", Catalogue);
            json.WriteStartArray("products");
            foreach (var row in Products)
            {
                json.WriteStartObject();
                json.WriteString("key", row.Key);
                WriteText(json, "publishedVersion", row.PublishedVersion);
                WriteText(json, "installedVersion", row.InstalledVersion);
                json.WriteStartArray("loadedVersions");
                foreach (var version in row.LoadedVersions) json.WriteStringValue(version);
                json.WriteEndArray();
                json.WriteString("state", row.State);
                json.WriteBoolean("activationPending", row.ActivationPending);
                json.WriteBoolean("retryable", row.Retryable);
                json.WriteString("message", row.Message);
                WriteText(json, "log", row.Log);
                json.WriteStartArray("profiles");
                foreach (var route in row.Profiles)
                {
                    json.WriteStartObject();
                    json.WriteString("key", route.Key);
                    json.WriteString("state", route.State);
                    WriteText(json, "route", route.Route);
                    WriteText(json, "pluginVersion", route.PluginVersion);
                    json.WriteBoolean("activationPending", route.ActivationPending);
                    if (route.Observed) json.WriteNull("guideLoaded");
                    WriteText(json, "reason", route.Reason);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteString("message", Message);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteText(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null) json.WriteNull(name); else json.WriteString(name, value);
    }
}

/// <summary>A file of a published release, downloaded and checked against that release's SHA256SUMS.txt.</summary>
public sealed record PublishedFile(string Tag, string Version, string Path, string Sha256);

/// <summary>
/// Tests only: the release a commit is about to publish, before it exists (the pack check). Catalog is that commit's
/// local catalogue (.claude-plugin/marketplace.json); Engines holds its engines and their SHA256SUMS.txt.
/// </summary>
public sealed record TestRelease(string Catalog, string Engines);

/// <summary>NexusMaintenance.exe's command line: --current, --force, and the test release's two folders, always together.</summary>
public sealed record MaintenanceOptions(bool Current, bool Force, TestRelease? TestRelease)
{
    public static bool TryParse(IReadOnlyList<string> args, out MaintenanceOptions options)
    {
        options = new MaintenanceOptions(false, false, null);
        var (current, force) = (false, false);
        string? catalog = null, engines = null;
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i].ToLowerInvariant();
            if (name == "--current" && !current) current = true;
            else if (name == "--force" && !force) force = true;
            else if (name is "--test-catalog" or "--test-engines" && i + 1 < args.Count && LocalFolder(args[i + 1]) is { } folder)
            {
                if (name == "--test-catalog" && catalog is null) catalog = folder;
                else if (name == "--test-engines" && engines is null) engines = folder;
                else return false;
                i++;
            }
            else return false;
        }
        if (catalog is null != engines is null) return false;
        options = new MaintenanceOptions(current, force, catalog is null ? null : new TestRelease(catalog, engines!));
        return true;
    }

    /// <summary>An existing absolute local folder (drive letter, no network share), normalized; otherwise null.</summary>
    static string? LocalFolder(string value) =>
        value.Length > 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/' && Directory.Exists(value)
            ? Path.GetFullPath(value).TrimEnd('\\', '/') : null;
}

/// <summary>The running engines a product's AI connections use, as observed (never inferred from installed files).</summary>
public sealed record LoadedEngines(IReadOnlyList<string> Versions, bool Unknown, IReadOnlyList<LoadedConnection> Agents);

public sealed record LoadedConnection(string Agent, string? Version, string? ProcessStartUtc);

/// <summary>The pure rules of releases and installers: tags, checksum manifests, a script's declared parameters.</summary>
public static partial class ReleaseRules
{
    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    public static partial Regex StableTag();

    [GeneratedRegex(@"^v\d+\.\d+\.\d+(-dev\.\d+)?$")]
    public static partial Regex InstallableTag();

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+)$")]
    private static partial Regex TagNumber();

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\s+\*?(\S+)\s*$")]
    private static partial Regex ChecksumLine();

    /// <summary>The x.y.z of a stable release tag vX.Y.Z.</summary>
    public static string TagVersion(string tag) => TagNumber().Match(tag) is { Success: true } match
        ? match.Groups[1].Value : throw new InvalidDataException($"Unsupported release tag: {tag}");

    /// <summary>The SHA-256 a SHA256SUMS.txt lists for a file; a malformed or ambiguous manifest is refused.</summary>
    public static string ExpectedSha256(string manifest, string name)
    {
        string? expected = null;
        foreach (var line in manifest.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0))
        {
            var match = ChecksumLine().Match(line);
            if (!match.Success) throw new InvalidDataException("Malformed checksum file.");
            if (match.Groups[2].Value != name) continue;
            if (expected is not null) throw new InvalidDataException($"{name} is listed twice in the checksum file.");
            expected = match.Groups[1].Value.ToLowerInvariant();
        }
        return expected ?? throw new InvalidDataException($"{name} is absent from the checksum file.");
    }

    /// <summary>
    /// gh attestation verify's exit codes, one per accepted signer: one success proves the provenance; exit code 4 (gh
    /// not signed in, attestations unreadable) means it could not be checked, as without gh; anything else is a failure.
    /// </summary>
    public static bool ProvenanceAccepted(IReadOnlyList<int> codes) => codes.Contains(0) || codes.Count > 0 && codes[^1] == 4;

    /// <summary>A version of x.y.z numbers, for comparisons.</summary>
    public static Version Number(string version) => Version.Parse(version);

    /// <summary>
    /// The parameters a PowerShell script declares in its top-level param() block, read without running it: comments
    /// and strings skipped, each parameter the first variable after an opening parenthesis or a comma at the block's own
    /// level (attributes and default values are not parameters).
    /// </summary>
    public static IReadOnlyList<string> ScriptParameters(string script)
    {
        var at = 0;
        // Before param: comments, #requires, attributes such as [CmdletBinding()] and blank lines only.
        while (true)
        {
            at = SkipSpace(script, at);
            if (at >= script.Length) return [];
            if (script[at] == '[') { at = SkipBracket(script, at); continue; }
            break;
        }
        if (string.Compare(script, at, "param", 0, 5, StringComparison.OrdinalIgnoreCase) != 0) return [];
        at = SkipSpace(script, at + 5);
        if (at >= script.Length || script[at] != '(') return [];
        var names = new List<string>();
        var (depth, expecting) = (0, true);
        for (; at < script.Length; at++)
        {
            var c = script[at];
            if (c == '#' || c == '<' && at + 1 < script.Length && script[at + 1] == '#') { at = SkipSpace(script, at) - 1; continue; }
            if (c is '\'' or '"') { at = SkipString(script, at) - 1; continue; }
            if (c is '(' or '[' or '{') { depth++; continue; }
            if (c is ')' or ']' or '}')
            {
                if (--depth == 0) return names;
                continue;
            }
            if (depth == 1 && c == ',') { expecting = true; continue; }
            if (depth == 1 && expecting && c == '$')
            {
                var end = at + 1;
                while (end < script.Length && (char.IsLetterOrDigit(script[end]) || script[end] == '_')) end++;
                if (end > at + 1) names.Add(script[(at + 1)..end]);
                expecting = false;
                at = end - 1;
            }
        }
        throw new InvalidDataException("Unterminated param block.");
    }

    static int SkipSpace(string text, int at)
    {
        while (at < text.Length)
        {
            if (char.IsWhiteSpace(text[at])) at++;
            else if (text[at] == '#')
            {
                while (at < text.Length && text[at] != '\n') at++;
            }
            else if (text[at] == '<' && at + 1 < text.Length && text[at + 1] == '#')
            {
                var end = text.IndexOf("#>", at + 2, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 2;
            }
            else break;
        }
        return at;
    }

    static int SkipBracket(string text, int at)
    {
        var depth = 0;
        for (; at < text.Length; at++)
        {
            if (text[at] is '\'' or '"') { at = SkipString(text, at) - 1; continue; }
            if (text[at] is '[' or '(') depth++;
            else if (text[at] is ']' or ')' && --depth == 0) return at + 1;
        }
        return at;
    }

    static int SkipString(string text, int at)
    {
        var quote = text[at];
        for (at++; at < text.Length; at++)
        {
            if (quote == '"' && text[at] == '`') { at++; continue; }
            if (text[at] != quote) continue;
            if (at + 1 < text.Length && text[at + 1] == quote) { at++; continue; } // doubled quote
            return at + 1;
        }
        return at;
    }
}
