using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>Codex's config.toml in a form this code does not understand: nothing is inferred or changed from it.</summary>
public sealed class CodexTomlException(string message) : FormatException(message);

/// <summary>One TOML table of Codex's config.toml, with its character range in the original text.</summary>
public sealed record CodexTomlTable(IReadOnlyList<string> Path, int Index, int Length, string Body,
    IReadOnlyList<CodexTomlAssignment> Assignments, bool Array);

/// <summary>A "key = value" line of a table; Value is the raw rest of the line (multiline values: its first line).</summary>
public sealed record CodexTomlAssignment(IReadOnlyList<string> Path, string Value);

/// <summary>A product's MCP server declaration found in config.toml. Unsupported: inline or dotted forms.</summary>
public sealed record CodexServerTable(int Index, int Length, string Body, string Sub,
    IReadOnlyList<CodexTomlAssignment> Assignments, bool Unsupported);

/// <summary>
/// A small lexical scanner of Codex's config.toml, not a regex over the whole document: a fake header inside a
/// multiline string or array never becomes a table. Values are never evaluated; anything outside this grammar is a
/// visible failure, so no configuration is changed from a guess.
/// </summary>
public static partial class CodexToml
{
    const string KeyPattern = @"(?:""(?:[^""\\\r\n]|\\.)*""|'[^'\r\n]*'|[A-Za-z0-9_-]+)";

    [GeneratedRegex(@"^(""(?:[^""\\\r\n]|\\.)*""|'[^'\r\n]*'|[A-Za-z0-9_-]+)[\t ]*")]
    private static partial Regex KeyToken();

    [GeneratedRegex(@"(?<!\\)((?:\\\\)*)\\U([0-9A-Fa-f]{8})")]
    private static partial Regex LongUnicodeEscape();

    [GeneratedRegex(@"[^\r\n]*(?:\r?\n|\z)")]
    private static partial Regex Lines();

    [GeneratedRegex(@"^[\t ]*\[")]
    private static partial Regex HeaderStart();

    [GeneratedRegex(@"^[\t ]*(?<open>\[\[?)[\t ]*(?<path>" + KeyPattern + @"(?:[\t ]*\.[\t ]*" + KeyPattern + @")*)[\t ]*(?<close>\]\]?)[\t ]*(?:#.*)?$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^[\t ]*(#|$)")]
    private static partial Regex BlankOrComment();

    [GeneratedRegex(@"^[\t ]*(?<key>" + KeyPattern + @"(?:[\t ]*\.[\t ]*" + KeyPattern + @")*)[\t ]*=[\t ]*(?<value>.*)$")]
    private static partial Regex Assignment();

    /// <summary>A dotted TOML key, with literal and basic quoted parts, escapes and spaces around the dots.</summary>
    public static IReadOnlyList<string> KeyPath(string text)
    {
        var keys = new List<string>();
        var rest = text.Trim();
        while (rest.Length > 0)
        {
            var key = KeyToken().Match(rest);
            if (!key.Success) throw new CodexTomlException("Unsupported Codex TOML key; configuration was kept.");
            var value = key.Groups[1].Value;
            if (value.StartsWith('\'')) value = value[1..^1];
            else if (value.StartsWith('"'))
            {
                // JSON and TOML basic keys share their escapes, except TOML's eight-digit Unicode form.
                value = LongUnicodeEscape().Replace(value, match => match.Groups[1].Value +
                    char.ConvertFromUtf32(int.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                value = JsonString(value);
            }
            keys.Add(value);
            rest = rest[key.Length..];
            if (rest.Length == 0) break;
            if (!rest.StartsWith('.')) throw new CodexTomlException("Unsupported Codex TOML key; configuration was kept.");
            rest = rest[1..].TrimStart();
            if (rest.Length == 0) throw new CodexTomlException("Incomplete Codex TOML key; configuration was kept.");
        }
        return keys;
    }

    static string JsonString(string quoted)
    {
        try
        {
            using var document = JsonDocument.Parse(quoted);
            return document.RootElement.GetString() ?? throw new CodexTomlException("Unsupported Codex TOML string; configuration was kept.");
        }
        catch (JsonException)
        {
            throw new CodexTomlException("Unsupported Codex TOML string; configuration was kept.");
        }
    }

    /// <summary>The tables of the document in order; the first one is the root table (no header).</summary>
    public static IReadOnlyList<CodexTomlTable> Tables(string text)
    {
        var tables = new List<CodexTomlTable>();
        var (path, index, body, assignments, array) = (new List<string>() as IReadOnlyList<string>, 0, new StringBuilder(), new List<CodexTomlAssignment>(), false);
        var quote = "";
        var depth = 0;
        var offset = 0;
        foreach (Match lineMatch in Lines().Matches(text))
        {
            if (lineMatch.Length == 0) continue;
            var line = lineMatch.Value.TrimEnd('\r', '\n');
            if (quote.Length == 0 && depth == 0 && HeaderStart().IsMatch(line))
            {
                var header = Header().Match(line);
                if (!header.Success || header.Groups["open"].Length != header.Groups["close"].Length)
                    throw new CodexTomlException("Unsupported Codex TOML table; configuration was kept.");
                tables.Add(new CodexTomlTable(path, index, offset - index, body.ToString(), assignments, array));
                (path, index, body, assignments, array) = (KeyPath(header.Groups["path"].Value), offset, new StringBuilder(), [], header.Groups["open"].Length == 2);
                offset += lineMatch.Length;
                continue;
            }
            if (quote.Length == 0 && depth == 0 && !BlankOrComment().IsMatch(line))
            {
                var assignment = Assignment().Match(line);
                if (!assignment.Success) throw new CodexTomlException("Unsupported Codex TOML assignment; configuration was kept.");
                assignments.Add(new CodexTomlAssignment(KeyPath(assignment.Groups["key"].Value), assignment.Groups["value"].Value));
            }
            body.Append(lineMatch.Value);
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quote.Length > 0)
                {
                    if (quote[0] == '"' && c == '\\') { i++; continue; }
                    if (quote.Length == 3)
                    {
                        if (i + 2 < line.Length && string.CompareOrdinal(line, i, quote, 0, 3) == 0)
                        {
                            i += 2;
                            while (i + 1 < line.Length && line[i + 1] == quote[0]) i++;
                            quote = "";
                        }
                    }
                    else if (c == quote[0]) quote = "";
                    continue;
                }
                if (c == '#') break;
                if (c is '"' or '\'')
                {
                    quote = c.ToString();
                    if (i + 2 < line.Length && string.CompareOrdinal(line, i, new string(c, 3), 0, 3) == 0) { quote = new string(c, 3); i += 2; }
                }
                else if (c is '[' or '{') depth++;
                else if (c is ']' or '}') depth--;
            }
            if (quote.Length == 1 || depth < 0) throw new CodexTomlException("Incomplete Codex TOML value; configuration was kept.");
            offset += lineMatch.Length;
        }
        if (quote.Length > 0 || depth != 0) throw new CodexTomlException("Incomplete Codex TOML value; configuration was kept.");
        tables.Add(new CodexTomlTable(path, index, text.Length - index, body.ToString(), assignments, array));
        return tables;
    }

    /// <summary>
    /// A plugin's state in config.toml: "enabled", "disabled" (turned off by the user), "unknown" (an inline form
    /// this code does not edit) or "missing". With a catalog, only that catalog's plugin counts.
    /// </summary>
    public static string PluginState(string text, string name, string? catalog = null)
    {
        var (found, unknown, disabled) = (false, false, false);
        foreach (var table in Tables(text))
        {
            if (table.Path.Count >= 2 && table.Path[0] == "plugins" && table.Path[1].StartsWith(name + "@", StringComparison.Ordinal) &&
                (catalog is null || table.Path[1] == name + "@" + catalog))
            {
                if (table.Path.Count != 2 || table.Array) unknown = true; else found = true;
            }
            foreach (var assignment in table.Assignments)
            {
                var path = table.Path.Concat(assignment.Path).ToList();
                if (path[0] != "plugins") continue;
                if (path.Count == 1) { unknown = true; continue; } // inline plugins = {...}: kept, never inferred missing
                if (!path[1].StartsWith(name + "@", StringComparison.Ordinal) || catalog is not null && path[1] != name + "@" + catalog) continue;
                if (path.Count == 2) { unknown = true; continue; } // inline plugin = {...}
                found = true;
                if (path.Count == 3 && path[2] == "enabled")
                {
                    if (BooleanValue(assignment.Value) is { } enabled) disabled |= !enabled;
                    else unknown = true;
                }
            }
        }
        return disabled ? "disabled" : unknown ? "unknown" : found ? "enabled" : "missing";
    }

    [GeneratedRegex(@"^(?<value>true|false)[\t ]*(?:#.*)?$")]
    private static partial Regex Boolean();

    /// <summary>true or false (a trailing comment allowed), or null for any other value.</summary>
    public static bool? BooleanValue(string value) => Boolean().Match(value) is { Success: true } match ? match.Groups["value"].Value == "true" : null;

    /// <summary>The declarations of one MCP server: its tables and sub-tables, or an inline or dotted form (unsupported).</summary>
    public static IReadOnlyList<CodexServerTable> ServerTables(string text, string name)
    {
        var found = new List<CodexServerTable>();
        foreach (var table in Tables(text))
        {
            if (table.Path.Count >= 2 && table.Path[0] == "mcp_servers" && table.Path[1] == name)
            {
                var sub = table.Path.Count > 2 ? "." + string.Join('.', table.Path.Skip(2)) : "";
                found.Add(new CodexServerTable(table.Index, table.Length, table.Body, sub, table.Assignments, table.Array));
                continue;
            }
            foreach (var assignment in table.Assignments)
            {
                var path = table.Path.Concat(assignment.Path).ToList();
                if (path[0] == "mcp_servers" && (path.Count == 1 || path[1] == name))
                {
                    // Inline/dotted declarations are valid TOML, but are not installer-owned editable tables.
                    found.Add(new CodexServerTable(table.Index, table.Length, table.Body, "", table.Assignments, true));
                    break;
                }
            }
        }
        return found;
    }

    [GeneratedRegex(@"^'([^']*)'$")]
    private static partial Regex LiteralString();

    [GeneratedRegex(@"^""(?:[^""\\]|\\.)*""$")]
    private static partial Regex BasicString();

    /// <summary>A one-line TOML string value; anything else is refused.</summary>
    public static string StringValue(string value)
    {
        if (LiteralString().Match(value) is { Success: true } literal) return literal.Groups[1].Value;
        if (BasicString().IsMatch(value)) return JsonString(value);
        throw new CodexTomlException("Not a simple TOML string.");
    }

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$")]
    private static partial Regex SimpleLine();

    [GeneratedRegex(@"^\s*(""(?:[^""\\]|\\.)*""|'[^']*')\s*(,\s*|$)")]
    private static partial Regex ArrayItem();

    [GeneratedRegex(@"^(--python 3\.14 )?nexus-android(==\d+\.\d+\.\d+)?$")]
    private static partial Regex AndroidPackage();

    [GeneratedRegex(@"^nexus-windows-codex(@latest)?$")]
    private static partial Regex WindowsPackage();

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex BrowserVersionFolder();

    static readonly string[] FormerAndroidLaunchers = ["uvx", "uvx.exe"];

    static readonly Dictionary<string, string> Daemons = new()
    {
        ["nexus-relay"] = "http://127.0.0.1:16181/mcp?agent=codex",
        ["nexus-android"] = "http://127.0.0.1:16180/mcp",
        ["nexus-browser"] = "http://127.0.0.1:16182/mcp?agent=codex",
    };

    /// <summary>
    /// Only complete declarations written by the official installers. A same-named Docker command, extra argument,
    /// custom URL, environment variable or unsupported TOML form belongs to the user and is kept.
    /// </summary>
    public static bool IsOwnedServer(string text, string name, UserFolders folders)
    {
        var tables = ServerTables(text, name);
        if (tables.Count == 0) return false;
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        try
        {
            foreach (var table in tables)
            {
                if (table.Unsupported) return false;
                if (table.Sub.Length > 0 && table.Sub != ".env") return false;
                var values = table.Sub.Length > 0 ? environment : root;
                foreach (var line in table.Body.Split('\n').Select(l => l.TrimEnd('\r')))
                {
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
                    var match = SimpleLine().Match(line);
                    if (!match.Success) return false;
                    var (key, value) = (match.Groups[1].Value, match.Groups[2].Value);
                    if (values.ContainsKey(key)) return false;
                    if (key == "args" && table.Sub.Length == 0)
                    {
                        if (!(value.StartsWith('[') && value.EndsWith(']'))) return false;
                        var items = value[1..^1];
                        var arguments = new List<string>();
                        while (items.Trim().Length > 0)
                        {
                            var item = ArrayItem().Match(items);
                            if (!item.Success) return false;
                            arguments.Add(StringValue(item.Groups[1].Value));
                            items = items[item.Length..];
                        }
                        values[key] = arguments;
                    }
                    else if (key == "enabled" && table.Sub.Length == 0)
                    {
                        if (value != "true") return false;
                        values[key] = true;
                    }
                    else if (key == "startup_timeout_sec" && table.Sub.Length == 0)
                    {
                        if (!value.All(char.IsAsciiDigit) || value.Length == 0) return false;
                        values[key] = value;
                    }
                    else values[key] = StringValue(value);
                }
            }
        }
        catch (CodexTomlException) { return false; }
        // The forms the official installers write today (native engine, local daemon) and wrote before (npm and PyPI
        // launchers, recognised only to be replaced by the plugin, never started).
        if (root.TryGetValue("url", out var url))
        {
            string[] allowedUrl = ["url", "bearer_token_env_var", "startup_timeout_sec", "enabled"];
            if (root.Keys.Any(k => !allowedUrl.Contains(k))) return false;
            return environment.Count == 0 && Daemons.TryGetValue(name, out var daemon) &&
                string.Equals(url as string, daemon, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(root.GetValueOrDefault("bearer_token_env_var") as string, "NEXUS_MCP_TOKEN", StringComparison.OrdinalIgnoreCase);
        }
        string[] allowed = ["command", "args", "startup_timeout_sec", "enabled"];
        if (root.Keys.Any(k => !allowed.Contains(k))) return false;
        var commandPath = root.GetValueOrDefault("command") as string ?? "";
        var command = Path.GetFileName(commandPath).ToLowerInvariant();
        var args = root.GetValueOrDefault("args") as List<string> ?? [];
        switch (name)
        {
            case "nexus-windows":
            {
                if (environment.Count > 0) return false;
                var native = Path.Combine(folders.Home, ".nexus", "nexus-windows");
                if (command == "nexuswindows.exe")
                {
                    var directory = Path.GetDirectoryName(commandPath);
                    if (string.IsNullOrEmpty(directory)) return false;
                    var folder = Path.GetFullPath(directory);
                    return (folder.Equals(native, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetDirectoryName(folder), Path.Combine(native, "native"), StringComparison.OrdinalIgnoreCase)) &&
                        string.Join(' ', args) == "--mcp --agent codex";
                }
                return command is "npx" or "npx.cmd" or "npx.exe" && args.Count == 2 &&
                    args[0].ToLowerInvariant() is "-y" or "--yes" && WindowsPackage().IsMatch(args[1]);
            }
            case "nexus-android":
                // Recognised only to be replaced by the plugin (the former PyPI launcher), never started.
                return environment.Count == 0 && FormerAndroidLaunchers.Contains(command) && AndroidPackage().IsMatch(string.Join(' ', args));
            case "nexus-browser":
            {
                if (environment.Count != 1 || !string.Equals(environment.GetValueOrDefault("NEXUS_BROWSER_AGENT") as string, "Codex", StringComparison.OrdinalIgnoreCase)) return false;
                // Native 2.33.0 wrote this direct stdio declaration beside an already installed catalog plugin. Only
                // its immutable release directory, exact server mode and Codex tab group belong to the installer.
                if (command == "nexusbrowser.exe" && args.Count == 1 && args[0] == "--mcp")
                {
                    if (!Path.IsPathRooted(commandPath)) return false;
                    var folder = Path.GetFullPath(Path.GetDirectoryName(commandPath)!);
                    var versions = Path.GetFullPath(Path.Combine(folders.LocalAppData, "NexusBrowser", "versions"));
                    return string.Equals(Path.GetDirectoryName(folder), versions, StringComparison.OrdinalIgnoreCase) &&
                        BrowserVersionFolder().IsMatch(Path.GetFileName(folder));
                }
                if (args.Count != 2 || !args[0].Equals("-B", StringComparison.OrdinalIgnoreCase)) return false;
                var script = Path.Combine(folders.LocalAppData, "NexusBrowser", "app", "client", "mcp_server.py");
                string[] pythons = [Path.Combine(folders.LocalAppData, "NexusBrowser", "venv", "Scripts", "python.exe"),
                    Path.Combine(folders.Home, ".nexus", "nexus-browser", "venv", "Scripts", "python.exe")];
                return pythons.Any(python => commandPath.Equals(python, StringComparison.OrdinalIgnoreCase)) &&
                    args[1].Equals(script, StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }
}
