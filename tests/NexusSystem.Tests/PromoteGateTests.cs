using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace NexusSystem.Tests;

/// <summary>
/// product-promote.yml publishes nothing before every check of the build's commit is green (NexusBrowser 2.35.28 was
/// promoted with its Tests workflow red). Its checks step, exactly as the workflow runs it (Python), against a local
/// stand-in for the GitHub API: red refused, running awaited, absent or unfinished refused, the caller's own runs left out.
/// </summary>
public class PromoteGateTests
{
    const string Workflow = ".github/workflows/product-promote.yml";
    const string Commit = "0123456789abcdef0123456789abcdef01234567";
    const string RunId = "4242";

    static YamlMappingNode Jobs()
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(Repository.Text(Workflow)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        return (YamlMappingNode)root.Children[new YamlScalarNode("jobs")];
    }

    static YamlMappingNode Job(string name) => (YamlMappingNode)Jobs().Children[new YamlScalarNode(name)];

    static string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value!;

    static YamlMappingNode GateStep() => ((YamlSequenceNode)Job("checks").Children[new YamlScalarNode("steps")])
        .Cast<YamlMappingNode>().Single(step => step.Children.TryGetValue(new YamlScalarNode("name"), out var name)
            && ((YamlScalarNode)name).Value == "Every check of the commit is green");

    [Fact]
    public void PromotionWaitsForTheChecksJob()
    {
        var promote = Job("promote");
        var needs = ((YamlSequenceNode)promote.Children[new YamlScalarNode("needs")]).Select(n => ((YamlScalarNode)n).Value);
        Assert.Contains("checks", needs);
        Assert.Equal("needs.resolve.outputs.commit != ''", Scalar(promote, "if"));
        // The only job that can publish never runs when checks failed or was skipped (default success() of its needs).
        Assert.DoesNotContain("always()", Scalar(promote, "if"), StringComparison.Ordinal);
        var permissions = (YamlMappingNode)Job("checks").Children[new YamlScalarNode("permissions")];
        Assert.Equal(new Dictionary<string, string> { ["actions"] = "read", ["checks"] = "read", ["contents"] = "read", ["statuses"] = "read" },
            permissions.Children.ToDictionary(p => ((YamlScalarNode)p.Key).Value!, p => ((YamlScalarNode)p.Value).Value!));
        var step = GateStep();
        Assert.Equal("python", Scalar(step, "shell"));
        var env = (YamlMappingNode)step.Children[new YamlScalarNode("env")];
        Assert.Equal("${{ github.run_id }}", Scalar(env, "RUN_ID"));
        Assert.Equal("${{ needs.resolve.outputs.commit }}", Scalar(env, "COMMIT"));
    }

    /// <summary>The GitHub API as the checks step reads it, one survey after another.</summary>
    sealed class FakeApi : IDisposable
    {
        readonly HttpListener listener = new();
        readonly Task loop;
        public readonly string Url;
        public int Surveys;
        public string OwnPath = ".github/workflows/release.yml";
        public string[] Files = ["release.yml", "tests.yml", "security.yml", "codeql.yml"];
        public Func<int, JsonArray> Runs = _ => [];
        public Func<int, JsonArray> CheckRuns = _ => [];
        public JsonArray? CheckRunsPage2;
        public Func<int, JsonArray> Statuses = _ => [];
        public readonly List<string> Unauthorized = [];

        public FakeApi()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://localhost:{port}";
            listener.Prefixes.Add(Url + "/");
            listener.Start();
            loop = Task.Run(Serve);
        }

        async Task Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch { return; }
                var path = context.Request.Url!.AbsolutePath;
                var query = context.Request.Url.Query;
                if (context.Request.Headers["Authorization"] != "Bearer test-token") Unauthorized.Add(path);
                const string prefix = "/repos/owner/Product/";
                JsonNode? body = null;
                if (path == $"{prefix}actions/runs/{RunId}")
                {
                    Surveys++;
                    body = new JsonObject { ["path"] = OwnPath + "@refs/heads/main" };
                }
                else if (path == $"{prefix}contents/.github/workflows" && query.Contains($"ref={Commit}"))
                    body = new JsonArray(Files.Select(f => (JsonNode)new JsonObject { ["name"] = f, ["type"] = "file" }).ToArray());
                else if (path == $"{prefix}actions/runs" && query.Contains($"head_sha={Commit}"))
                    body = new JsonObject { ["workflow_runs"] = Runs(Surveys) };
                else if (path == $"{prefix}commits/{Commit}/check-runs")
                {
                    if (query.Contains("page=2")) body = new JsonObject { ["check_runs"] = CheckRunsPage2 };
                    else
                    {
                        body = new JsonObject { ["check_runs"] = CheckRuns(Surveys) };
                        if (CheckRunsPage2 is not null)
                            context.Response.Headers["Link"] = $"<{Url}{prefix}commits/{Commit}/check-runs?per_page=100&page=2>; rel=\"next\"";
                    }
                }
                else if (path == $"{prefix}commits/{Commit}/status")
                    body = new JsonObject { ["statuses"] = Statuses(Surveys) };
                var bytes = Encoding.UTF8.GetBytes(body?.ToJsonString() ?? "{\"message\":\"Not Found\"}");
                context.Response.StatusCode = body is null ? 404 : 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            listener.Stop();
            listener.Close();
            try { loop.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        }
    }

    static JsonObject Run(long id, string file, string status, string? conclusion = null, string? name = null) => new()
    {
        ["id"] = id,
        ["name"] = name ?? Path.GetFileNameWithoutExtension(file),
        ["path"] = $".github/workflows/{file}",
        ["status"] = status,
        ["conclusion"] = conclusion,
        ["html_url"] = $"https://github.com/owner/Product/actions/runs/{id}",
    };

    static JsonArray Green() =>
    [
        Run(10, "tests.yml", "completed", "success", "Tests"),
        Run(11, "security.yml", "completed", "success", "Security"),
        Run(12, "codeql.yml", "completed", "success", "CodeQL"),
    ];

    static (int ExitCode, string Output, string Summary) Gate(FakeApi api, double timeoutMinutes = 0.05)
    {
        var run = Scalar(GateStep(), "run");
        var folder = Directory.CreateTempSubdirectory("nexus-promote-gate-");
        try
        {
            var script = Path.Combine(folder.FullName, "gate.py");
            File.WriteAllText(script, run);
            var summary = Path.Combine(folder.FullName, "summary.md");
            var start = new ProcessStartInfo("python")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-I");
            start.ArgumentList.Add(script);
            start.Environment["GITHUB_API_URL"] = api.Url;
            start.Environment["GH_TOKEN"] = "test-token";
            start.Environment["REPOSITORY"] = "owner/Product";
            start.Environment["COMMIT"] = Commit;
            start.Environment["DEV"] = "v1.2.3-dev.7";
            start.Environment["RUN_ID"] = RunId;
            start.Environment["REQUIRED"] = "tests.yml security.yml codeql.yml";
            start.Environment["TIMEOUT_MINUTES"] = timeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["POLL_SECONDS"] = "0.1";
            start.Environment["GITHUB_STEP_SUMMARY"] = summary;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The checks step did not finish within 2 min");
            }
            process.WaitForExit();
            var text = output.Result + error.Result;
            TestContext.Current.TestOutputHelper?.WriteLine(text);
            Assert.Empty(api.Unauthorized);
            return (process.ExitCode, text, File.Exists(summary) ? File.ReadAllText(summary) : "");
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void EveryCheckGreenPromotes()
    {
        using var api = new FakeApi
        {
            Runs = _ => Green(),
            CheckRuns = _ => [new JsonObject { ["name"] = "Socket", ["status"] = "completed", ["conclusion"] = "neutral", ["app"] = new JsonObject { ["slug"] = "socket-security" } }],
            Statuses = _ => [new JsonObject { ["context"] = "ci/external", ["state"] = "success" }],
        };
        var (exitCode, output, _) = Gate(api);
        Assert.Equal(0, exitCode);
        Assert.Contains("Every check of", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RedTestsWorkflowRefuses()
    {
        // NexusBrowser 2.35.28: Release green, Tests red on the same commit.
        using var api = new FakeApi
        {
            Runs = _ => [Run(10, "tests.yml", "completed", "failure", "Tests"), Run(11, "security.yml", "completed", "success"),
                Run(12, "codeql.yml", "completed", "success"), Run(13, "release.yml", "in_progress", null, "Release")],
        };
        var (exitCode, output, summary) = Gate(api, timeoutMinutes: 5);
        Assert.Equal(1, exitCode);
        Assert.Contains("v1.2.3-dev.7 is not promoted", output, StringComparison.Ordinal);
        Assert.Contains("workflow Tests (.github/workflows/tests.yml): completed, failure", output, StringComparison.Ordinal);
        Assert.Contains("workflow Tests", summary, StringComparison.Ordinal);
        Assert.Equal(1, api.Surveys);  // red is refused at once, even with a long timeout
    }

    [Fact]
    public void RunningChecksAreAwaited()
    {
        using var api = new FakeApi
        {
            Runs = survey => survey < 3
                ? [Run(10, "tests.yml", "in_progress", null, "Tests"), Run(11, "security.yml", "completed", "success"), Run(12, "codeql.yml", "queued")]
                : Green(),
            Statuses = survey => [new JsonObject { ["context"] = "ci/external", ["state"] = survey < 2 ? "pending" : "success" }],
        };
        var (exitCode, output, _) = Gate(api, timeoutMinutes: 1);
        Assert.Equal(0, exitCode);
        Assert.Contains("Waiting for:", output, StringComparison.Ordinal);
        Assert.True(api.Surveys >= 3);
    }

    [Fact]
    public void ChecksStillRunningAtTheDeadlineRefuse()
    {
        using var api = new FakeApi { Runs = _ => [Run(10, "tests.yml", "in_progress", null, "Tests"), Run(11, "security.yml", "completed", "success"), Run(12, "codeql.yml", "completed", "success")] };
        var (exitCode, output, _) = Gate(api, timeoutMinutes: 0.01);
        Assert.Equal(1, exitCode);
        Assert.Contains("did not finish in time", output, StringComparison.Ordinal);
        Assert.Contains("workflow Tests (.github/workflows/tests.yml): in_progress", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredWorkflowThatNeverRanRefuses()
    {
        using var api = new FakeApi { Runs = _ => [Run(10, "tests.yml", "completed", "success"), Run(12, "codeql.yml", "completed", "success")] };
        var (exitCode, output, _) = Gate(api, timeoutMinutes: 0.01);
        Assert.Equal(1, exitCode);
        Assert.Contains("required workflow security.yml: no run on", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredWorkflowMissingFromTheRepositoryRefusesAtOnce()
    {
        using var api = new FakeApi { Files = ["release.yml", "tests.yml", "codeql.yml"], Runs = _ => Green() };
        var (exitCode, output, _) = Gate(api, timeoutMinutes: 5);
        Assert.Equal(1, exitCode);
        Assert.Contains("required workflow security.yml: no .github/workflows/security.yml", output, StringComparison.Ordinal);
        Assert.Equal(1, api.Surveys);
    }

    [Fact]
    public void CancelledUndeclaredWorkflowRefuses()
    {
        using var api = new FakeApi { Runs = _ => [.. Green().Select(r => r!.DeepClone()), Run(14, "install.yml", "completed", "cancelled", "Install")] };
        var (exitCode, output, _) = Gate(api);
        Assert.Equal(1, exitCode);
        Assert.Contains("workflow Install (.github/workflows/install.yml): completed, cancelled", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestRunOfAWorkflowCountsAndTheCallerIsLeftOut()
    {
        // A Tests run re-run green after a red one; an earlier Release run whose promotion was refused; Dependabot's
        // dynamic workflow on the same commit is not the product's.
        using var api = new FakeApi
        {
            Runs = _ => [Run(9, "tests.yml", "completed", "failure", "Tests"), .. Green().Select(r => r!.DeepClone()),
                Run(5, "release.yml", "completed", "failure", "Release"),
                new JsonObject { ["id"] = 20, ["name"] = "Dependabot Updates", ["path"] = "dynamic/dependabot/dependabot-updates", ["status"] = "completed", ["conclusion"] = "failure", ["html_url"] = "x" }],
        };
        var (exitCode, _, _) = Gate(api);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void FailedCommitStatusOrExternalCheckOnALaterPageRefuses()
    {
        using var status = new FakeApi { Runs = _ => Green(), Statuses = _ => [new JsonObject { ["context"] = "ci/external", ["state"] = "failure", ["target_url"] = "https://ci.example/1" }] };
        var (exitCode, output, _) = Gate(status);
        Assert.Equal(1, exitCode);
        Assert.Contains("status ci/external: failure", output, StringComparison.Ordinal);

        using var paged = new FakeApi
        {
            Runs = _ => Green(),
            CheckRuns = _ => [new JsonObject { ["name"] = "build (from Actions)", ["status"] = "completed", ["conclusion"] = "failure", ["app"] = new JsonObject { ["slug"] = "github-actions" } }],
            CheckRunsPage2 = [new JsonObject { ["name"] = "Analyzer", ["status"] = "completed", ["conclusion"] = "action_required", ["app"] = new JsonObject { ["slug"] = "external-app" }, ["html_url"] = "https://ci.example/2" }],
        };
        (exitCode, output, _) = Gate(paged);
        Assert.Equal(1, exitCode);
        Assert.Contains("check Analyzer (external-app): completed, action_required", output, StringComparison.Ordinal);
        Assert.DoesNotContain("build (from Actions)", output, StringComparison.Ordinal);  // counted through its workflow run
    }
}
