using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nexus.Core;

/// <summary>
/// What the hourly maintenance does outside its own decisions: GitHub releases, product installers, AI app command
/// lines, running processes and its files. Each member is virtual so the tests run the real decisions on a throwaway
/// profile without network, installers or AI processes.
/// </summary>
public partial class MaintenanceServices(UserFolders folders)
{
    public const string EngineName = "NexusMaintenance.exe";
    const string Owner = "pironjulien";

    public UserFolders Folders { get; } = folders;

    static readonly Lazy<HttpClient> Http = new(() =>
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusSystem-maintenance");
        return http;
    });

    /// <summary>This engine's version: the NexusSystem catalog release it was published with.</summary>
    public virtual string ManagerVersion => Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>The SHA-256 of the running engine's own file.</summary>
    public virtual string ManagerSha256() => Sha256(Environment.ProcessPath ?? throw new InvalidOperationException("The engine's own file is unknown."));

    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>The published engine of this PC's architecture: arm64 on an ARM64 Windows (even from an emulated process).</summary>
    public static string PublishedEngineName => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "NexusMaintenance-arm64.exe" : "NexusMaintenance-x64.exe";

    public virtual void WriteStatus(MaintenanceReceipt state)
    {
        var path = MaintenanceUpdates.StatusPath(Folders);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, state.ToJson(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The product state the app shows (~\.nexus\status\&lt;key&gt;.json): its other fields are kept.</summary>
    public virtual void WriteProductStatus(string key, string installed, string? published)
    {
        var path = Path.Combine(Folders.Nexus, "status", key + ".json");
        var record = (File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null) ?? [];
        record["product"] = key;
        record["version"] = installed;
        record["latest"] = published;
        record["state"] = "idle";
        record["checked_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        record["message"] = "up to date";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, Serialize(record) + "\n", new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    static string Serialize(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) node.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public virtual IReadOnlyList<string> FindAiApps() => AiApps.FindKeys(Folders);

    /// <summary>The user's daemon credential, from the user environment (an AI started before installation lacks it).</summary>
    public virtual string? Token() => Environment.GetEnvironmentVariable("NEXUS_MCP_TOKEN", EnvironmentVariableTarget.User) is { Length: > 0 } token ? token : null;

    [GeneratedRegex(@"/releases/tag/(v[0-9][^/?#]*)$")]
    private static partial Regex TagLocation();

    /// <summary>
    /// The latest release's tag from the redirect of github.com/&lt;repo&gt;/releases/latest: no GitHub API (60 anonymous
    /// requests an hour per IP); the API stays as a fallback.
    /// </summary>
    public virtual async Task<string> LatestTagAsync(string repository, CancellationToken cancel)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusSystem-maintenance");
            using var response = await http.GetAsync(new Uri($"https://github.com/{Owner}/{repository}/releases/latest"), cancel).ConfigureAwait(false);
            if (TagLocation().Match(response.Headers.Location?.ToString() ?? "") is { Success: true } tag) return tag.Groups[1].Value;
        }
        catch (HttpRequestException) { /* the API below */ }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { /* the API below */ }
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{Owner}/{repository}/releases/latest"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var answer = await Http.Value.SendAsync(request, cancel).ConfigureAwait(false);
        answer.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        return document.RootElement.Text("tag_name") ?? throw new InvalidDataException($"{repository} has no published release.");
    }

    static async Task<string> ManifestAsync(string repository, string tag, CancellationToken cancel) =>
        await Http.Value.GetStringAsync(new Uri($"https://github.com/{Owner}/{repository}/releases/download/{tag}/SHA256SUMS.txt"), cancel).ConfigureAwait(false);

    /// <summary>A product's installer (get.ps1) of its latest release, checked against that release's checksums and,
    /// when the official gh tool is there, its build provenance.</summary>
    public virtual async Task<PublishedFile> PublishedInstallerAsync(string repository, string work, CancellationToken cancel)
    {
        var tag = await LatestTagAsync(repository, cancel).ConfigureAwait(false);
        if (!ReleaseRules.InstallableTag().IsMatch(tag)) throw new InvalidDataException($"Unsupported release tag for {repository}.");
        var folder = Path.Combine(work, repository);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "get.ps1");
        var expected = ReleaseRules.ExpectedSha256(await ManifestAsync(repository, tag, cancel).ConfigureAwait(false), "get.ps1");
        await DownloadVerifiedAsync(repository, tag, "get.ps1", file, expected, cancel).ConfigureAwait(false);
        return new PublishedFile(tag, tag[1..].Split('-')[0], file, expected);
    }

    /// <summary>The published maintenance engine's release and checksum, without downloading it.</summary>
    public virtual async Task<PublishedFile> PublishedEngineAsync(CancellationToken cancel)
    {
        var tag = await LatestTagAsync("NexusSystem", cancel).ConfigureAwait(false);
        var version = ReleaseRules.TagVersion(tag);
        var expected = ReleaseRules.ExpectedSha256(await ManifestAsync("NexusSystem", tag, cancel).ConfigureAwait(false), PublishedEngineName);
        return new PublishedFile(tag, version, PublishedEngineName, expected);
    }

    /// <summary>Downloads and checks the published engine, then makes it the durable one; returns its path.</summary>
    public virtual async Task<string> InstallPublishedEngineAsync(PublishedFile published, string work, CancellationToken cancel)
    {
        Directory.CreateDirectory(work);
        var download = Path.Combine(work, published.Path);
        await DownloadVerifiedAsync("NexusSystem", published.Tag, published.Path, download, published.Sha256, cancel).ConfigureAwait(false);
        var durable = MaintenanceUpdates.ExecutablePath(Folders);
        var temporary = $"{durable}.{Environment.ProcessId}.tmp";
        File.Copy(download, temporary, overwrite: true);
        // A running engine can be renamed, not replaced: the previous one steps aside for the published one.
        if (File.Exists(durable))
        {
            var previous = Path.Combine(Path.GetDirectoryName(durable)!, "NexusMaintenance.previous.exe");
            try { File.Delete(previous); }
            catch (IOException) { /* still running: it is replaced next time */ }
            File.Move(durable, previous, overwrite: true);
        }
        File.Move(temporary, durable, overwrite: true);
        return durable;
    }

    static async Task DownloadVerifiedAsync(string repository, string tag, string name, string file, string expected, CancellationToken cancel)
    {
        var bytes = await Http.Value.GetByteArrayAsync(new Uri($"https://github.com/{Owner}/{repository}/releases/download/{tag}/{name}"), cancel).ConfigureAwait(false);
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 mismatch for {name} of {repository} {tag}: the download is not the published file.");
        await File.WriteAllBytesAsync(file, bytes, cancel).ConfigureAwait(false);
        VerifyProvenance(repository, tag, file);
    }

    /// <summary>
    /// When the official gh tool is there, the build provenance is authenticated too: signed by NexusSystem's shared
    /// release workflow from main, or by the product's own workflow from its tag or from main. A failed attestation is
    /// fatal; it never degrades to checksums. Nothing installs or signs in gh.
    /// </summary>
    static void VerifyProvenance(string repository, string tag, string file)
    {
        if (AiApps.OnPath("gh") is not { } gh || !gh.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        string[][] attempts =
        [
            ["attestation", "verify", file, "--repo", $"{Owner}/{repository}", "--signer-repo", $"{Owner}/NexusSystem", "--source-ref", "refs/heads/main"],
            ["attestation", "verify", file, "--repo", $"{Owner}/{repository}", "--source-ref", $"refs/tags/{tag}"],
            ["attestation", "verify", file, "--repo", $"{Owner}/{repository}", "--source-ref", "refs/heads/main"],
        ];
        if (attempts.All(arguments => Run(gh, arguments).Code != 0)) throw new InvalidDataException($"Build provenance failed for {repository} {tag}.");
    }

    /// <summary>Starts the durable engine for this cycle once the lock is released, and waits for its verdict.</summary>
    public virtual Task<int> RunEngineAsync(string engine, CancellationToken cancel) => Task.Run(() => Run(engine, ["--current"]).Code, cancel);

    /// <summary>Exactly the parameters an update needs, read from the downloaded installer itself, never from its version.</summary>
    public virtual void CheckInstallerContract(string file, IReadOnlyList<string> parameters)
    {
        var declared = ReleaseRules.ScriptParameters(File.ReadAllText(file));
        if (parameters.Any(p => !declared.Contains(p, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The verified installer does not declare the required update-only contract; it was not executed.");
    }

    /// <summary>One file per installer run under maintenance\logs; only the newest 20 are kept.</summary>
    public virtual string InstallerLogPath(string key)
    {
        var folder = Path.Combine(MaintenanceUpdates.Root(Folders), "logs");
        Directory.CreateDirectory(folder);
        foreach (var old in new DirectoryInfo(folder).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(19))
        {
            try { old.Delete(); }
            catch (IOException) { /* in use: next time */ }
        }
        return Path.Combine(folder, $"{key}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    /// <summary>
    /// A product's published installer in a hidden Windows PowerShell, its output and errors written to the log
    /// (UTF-8), so a failed installer stays readable. NEXUS_PACK marks an unattended run.
    /// </summary>
    public virtual async Task<int> RunInstallerAsync(string what, string file, IReadOnlyList<string> arguments, string log, string? targets, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // The hidden child writes in its console's OEM code page.
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding((int)GetOEMCP()) ?? Encoding.UTF8;
        (start.StandardOutputEncoding, start.StandardErrorEncoding) = (encoding, encoding);
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", file }.Concat(arguments))
            start.ArgumentList.Add(argument);
        start.Environment["NEXUS_PACK"] = "1";
        if (targets is null) start.Environment.Remove("NEXUS_PACK_TARGETS"); else start.Environment["NEXUS_PACK_TARGETS"] = targets;
        if (Token() is { } token) start.Environment["NEXUS_MCP_TOKEN"] = token;
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        using var writer = new StreamWriter(log, false, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync($"{what}, {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"{file} {string.Join(' ', arguments)}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
        var gate = new object();
        void Write(string? line)
        {
            if (line is null) return;
            lock (gate) writer.WriteLine(line);
        }
        int code;
        using (var process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe did not start."))
        {
            process.OutputDataReceived += (_, e) => Write(e.Data);
            process.ErrorDataReceived += (_, e) => Write(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancel).ConfigureAwait(false);
            process.WaitForExit();
            code = process.ExitCode;
        }
        lock (gate)
        {
            writer.WriteLine();
            writer.WriteLine($"Exit code: {code}");
        }
        return code;
    }

    /// <summary>The installed version as the product's installer recorded it, checked against its files.</summary>
    public virtual string? InstalledVersion(MaintenanceProduct product)
    {
        switch (product.Key)
        {
            case "windows":
                return WindowsRuntime.ReadInstallation(Folders)?.Version;
            case "browser":
            {
                var record = JsonFiles.Read(Path.Combine(Folders.LocalAppData, "NexusBrowser", "current.json"));
                var fingerprint = record?.Text("fingerprint");
                var executable = record?.Text("exe");
                if (fingerprint is null || !Hex64().IsMatch(fingerprint) || string.IsNullOrEmpty(executable)) return null;
                var expected = Path.Combine(Folders.LocalAppData, "NexusBrowser", "versions", fingerprint[..16], "NexusBrowser.exe");
                if (!executable.Equals(expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(expected)) return null;
                var assembly = Path.Combine(Path.GetDirectoryName(expected)!, "NexusBrowser.dll");
                if (!File.Exists(assembly)) assembly = expected;
                return Sha256(assembly).Equals(fingerprint, StringComparison.OrdinalIgnoreCase) ? record!.Value.Text("version") : null;
            }
            case "keypad":
            {
                // The complete edition its installer copied into the Logitech plugin service's folder.
                var package = Path.Combine(Folders.LocalAppData, "Logi", "LogiPluginService", "Plugins", "NexusKeypad", "metadata", "LoupedeckPackage.yaml");
                if (!File.Exists(package)) return null;
                return File.ReadLines(package).Select(line => KeypadVersion().Match(line)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
            }
        }
        // The highest version among the engines the product's installer unpacked (the executable's own version).
        var (folder, pattern, name) = product.Key switch
        {
            "android" => (Path.Combine(Folders.Nexus, "nexus-android", "releases"), "*", "NexusAndroid.exe"),
            "relay" => (Path.Combine(Folders.LocalAppData, "NexusRelay", "versions"), "*", "NexusRelay.exe"),
            "agymail" => (Path.Combine(Folders.Nexus, "nexus-agymail", "app"), "*", "NexusAGYmail.exe"),
            "indesign" => (Path.Combine(Folders.Nexus, "nexus-indesign"), null, "NexusInDesign.exe"),
            "free" => (Path.Combine(Folders.Nexus, "nexus-free"), null, "NexusFree.exe"),
            _ => (null, null, null),
        };
        if (folder is null || !Directory.Exists(folder)) return null;
        var files = pattern is null ? [Path.Combine(folder, name!)] : Directory.EnumerateDirectories(folder, pattern).Select(d => Path.Combine(d, name!));
        return files.Where(File.Exists)
            .Select(f => EngineVersion().Match(FileVersionInfo.GetVersionInfo(f).ProductVersion ?? ""))
            .Where(m => m.Success).Select(m => Version.Parse(m.Groups[1].Value)).Order().LastOrDefault()?.ToString();
    }

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex Hex64();

    [GeneratedRegex(@"^version:\s*(\d+\.\d+\.\d+)\s*$")]
    private static partial Regex KeypadVersion();

    [GeneratedRegex(@"^(\d+\.\d+\.\d+)")]
    private static partial Regex EngineVersion();

    [GeneratedRegex(@"^release v\d+\.\d+\.\d+(-dev\.\d+)?$")]
    private static partial Regex ReleaseSource();

    /// <summary>
    /// How the installed bytes arrived: release, repository or unknown. A checkout need not still exist: moving its
    /// source is not permission to replace the installed development engine with a public release.
    /// </summary>
    public virtual string InstallationSource(MaintenanceProduct product)
    {
        if (product.Key == "windows")
        {
            var source = JsonFiles.Read(Path.Combine(Folders.Nexus, "nexus-windows", "installation.json"))?.Item("SourceRoot")?.ToString() ?? "";
            // A stable release carries its build's files unchanged, so its installer may name the build tag (-dev.N).
            if (ReleaseSource().IsMatch(source)) return "release";
            if (Path.IsPathRooted(source)) return "repository";
        }
        else if (product.Key == "browser" &&
            JsonFiles.Read(Path.Combine(Folders.LocalAppData, "NexusBrowser", "current.json"))?.Text("exe") is { Length: > 0 } executable &&
            Path.GetDirectoryName(executable) is { } folder)
        {
            var source = JsonFiles.Read(Path.Combine(folder, "installation.json"))?.Item("repository_root")?.ToString() ?? "";
            if (Path.IsPathRooted(source))
            {
                var published = Path.Combine(Folders.LocalAppData, "NexusBrowser", "app");
                return Path.GetFullPath(source).TrimEnd('\\', '/').Equals(Path.GetFullPath(published).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) &&
                    !Path.Exists(Path.Combine(source, ".git")) ? "release" : "repository";
            }
        }
        return "unknown";
    }

    /// <summary>The running engines of a product and the AI hosts that own them (live processes and endpoints only).</summary>
    public virtual LoadedEngines LoadedVersions(MaintenanceProduct product) => RuntimeObservation.Observe(product, Folders, NativeProcesses.Read());

    /// <summary>Refreshes the public catalogue plugins of the routes that use them (Codex and Claude Code).</summary>
    public virtual Task MaintainPluginsAsync(MaintenanceProduct product, IReadOnlyList<MaintenanceRoute> routes, CancellationToken cancel) =>
        Task.Run(() => PluginMaintenance.Refresh(product, routes, Folders), cancel);

    /// <summary>A program run hidden to its end, its output captured (UTF-8).</summary>
    public static (int Code, string Output, string Error) Run(string file, IEnumerable<string> arguments, string? input = null)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(file)} did not start.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
