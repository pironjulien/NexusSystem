using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class AntigravityDraftTests
{
    const string Prompt = "Installe et configure pour moi NexusWindows";

    [Fact]
    public void PortIsTheFirstLineOfDevToolsActivePort()
    {
        using var profile = new FakeProfile();
        Assert.Null(AntigravityDraft.Port(profile.Folders));
        profile.Write(@"AppData\Roaming\Antigravity\DevToolsActivePort", "9227\n/devtools/browser/abc\n");
        Assert.Equal(9227, AntigravityDraft.Port(profile.Folders));
        profile.Write(@"AppData\Roaming\Antigravity\DevToolsActivePort", "nope");
        Assert.Null(AntigravityDraft.Port(profile.Folders));
    }

    [Fact]
    public void HubPageSkipsTheStartScreen()
    {
        const string targets = """
            [{"type":"page","url":"data:text/html,splash","webSocketDebuggerUrl":"ws://127.0.0.1:1/devtools/page/splash"},
             {"type":"page","url":"https://127.0.0.1:52485/","webSocketDebuggerUrl":"ws://127.0.0.1:1/devtools/page/hub"}]
            """;
        Assert.Equal("ws://127.0.0.1:1/devtools/page/hub", AntigravityDraft.HubPage(targets));
        Assert.Null(AntigravityDraft.HubPage("""[{"type":"page","url":"data:text/html,splash","webSocketDebuggerUrl":"ws://x"}]"""));
        Assert.Null(AntigravityDraft.HubPage("not json"));
    }

    [Fact]
    public void SameTextIgnoresSpacesAndLineBreaks()
    {
        Assert.True(AntigravityDraft.SameText("Installe et configure\npour moi NexusWindows\n", Prompt));
        Assert.False(AntigravityDraft.SameText("Installe", Prompt));
        Assert.False(AntigravityDraft.SameText(null, Prompt));
    }

    [Fact]
    public async Task WritesInAnEmptyChatBoxWithoutSending()
    {
        using var antigravity = new FakeAntigravity("");
        using var profile = antigravity.Profile();
        Assert.True(await AntigravityDraft.WriteAsync(Prompt, profile.Folders, new HttpClient(), TimeSpan.FromSeconds(10)));
        Assert.Equal(Prompt, antigravity.Box);
        Assert.DoesNotContain(antigravity.Methods, m => m.StartsWith("Input.dispatchKeyEvent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpensANewConversationFirst()
    {
        using var antigravity = new FakeAntigravity("", "/c/older");
        using var profile = antigravity.Profile();
        Assert.True(await AntigravityDraft.WriteAsync(Prompt, profile.Folders, new HttpClient(), TimeSpan.FromSeconds(10)));
        Assert.Equal("/", antigravity.Path);
        Assert.Equal(Prompt, antigravity.Box);
    }

    [Fact]
    public async Task NeverOverwritesADraft()
    {
        using var antigravity = new FakeAntigravity("my own draft");
        using var profile = antigravity.Profile();
        Assert.False(await AntigravityDraft.WriteAsync(Prompt, profile.Folders, new HttpClient(), TimeSpan.FromSeconds(10)));
        Assert.Equal("my own draft", antigravity.Box);
        Assert.DoesNotContain("Input.insertText", antigravity.Methods);
    }

    [Fact]
    public async Task GivesUpWhenAntigravityNeverAnswers()
    {
        using var profile = new FakeProfile();
        profile.Write(@"AppData\Roaming\Antigravity\DevToolsActivePort", FreePort() + "\n");
        Assert.False(await AntigravityDraft.WriteAsync(Prompt, profile.Folders, new HttpClient(), TimeSpan.FromSeconds(2)));
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Antigravity's debugging port as this needs it: the target list and a page with one chat box.</summary>
    sealed class FakeAntigravity : IDisposable
    {
        readonly HttpListener http = new();
        readonly int port = FreePort();
        public string Box;
        public string Path;
        public List<string> Methods { get; } = [];

        public FakeAntigravity(string box, string path = "/")
        {
            Box = box;
            Path = path;
            http.Prefixes.Add($"http://127.0.0.1:{port}/");
            http.Start();
            _ = Task.Run(ServeAsync);
        }

        public FakeProfile Profile()
        {
            var profile = new FakeProfile();
            profile.Write(@"AppData\Roaming\Antigravity\DevToolsActivePort", $"{port}\n/devtools/browser/x\n");
            return profile;
        }

        async Task ServeAsync()
        {
            while (http.IsListening)
            {
                HttpListenerContext context;
                try { context = await http.GetContextAsync(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                if (context.Request.IsWebSocketRequest) { _ = Task.Run(() => PageAsync(context)); continue; }
                var list = $$"""
                    [{"type":"page","url":"data:text/html,splash","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/devtools/page/splash"},
                     {"type":"page","url":"https://127.0.0.1:52485/","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/devtools/page/hub"}]
                    """;
                var body = Encoding.UTF8.GetBytes(list);
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        async Task PageAsync(HttpListenerContext context)
        {
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            var buffer = new byte[64 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                var read = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (read.MessageType == WebSocketMessageType.Close) return;
                var call = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, read.Count))!.AsObject();
                var method = call["method"]!.GetValue<string>();
                lock (Methods) Methods.Add(method);
                JsonNode result = new JsonObject();
                if (method == "Input.insertText") Box += call["params"]!["text"]!.GetValue<string>();
                else if (method == "Runtime.evaluate")
                {
                    var expression = call["params"]!["expression"]!.GetValue<string>();
                    if (expression.Contains("location.pathname", StringComparison.Ordinal))
                    {
                        // An older conversation: its "New Conversation" button opens the home page.
                        var home = Path == "/" ? "home" : "clicked";
                        Path = "/";
                        result = new JsonObject { ["result"] = new JsonObject { ["type"] = "string", ["value"] = home } };
                    }
                    else result = new JsonObject { ["result"] = expression.Contains("focus()", StringComparison.Ordinal)
                        ? new JsonObject { ["type"] = "boolean", ["value"] = true }
                        : new JsonObject { ["type"] = "string", ["value"] = Box } };
                }
                var answer = new JsonObject { ["id"] = call["id"]!.GetValue<int>(), ["result"] = result };
                await socket.SendAsync(Encoding.UTF8.GetBytes(answer.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public void Dispose() => http.Close();
    }
}
