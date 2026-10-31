using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexus.Core;

/// <summary>
/// Writes a request in Antigravity's chat box, never sent: Antigravity has no link that fills its chat (its
/// antigravity:// links only import AI Studio projects), so the request goes through the local debugging port
/// Antigravity always opens for itself (DevToolsActivePort), the way NexusRelay writes in it. Only an empty box is
/// filled, in a new conversation: a draft the user started is never overwritten. The user presses Enter.
/// </summary>
public static class AntigravityDraft
{
    // The visible chat box of the agent manager (a contenteditable or a textarea), its text, and its focus.
    const string Composer = "[...document.querySelectorAll('[contenteditable=true],textarea')].filter(e=>e.getClientRects().length).pop()";
    const string ComposerText = $"(()=>{{const e={Composer};return e?(e.value??e.innerText):null}})()";
    const string Focus = $"(()=>{{const e={Composer};if(!e)return false;e.focus();return true}})()";
    // A new conversation: the home page, or Antigravity's own "New Conversation" button when it shows an older one.
    const string Home = "(()=>{if(location.pathname==='/')return 'home';const b=[...document.querySelectorAll('[data-testid=app-icon-new-conversation-button]')].find(e=>e.getClientRects().length);if(!b)return 'none';b.click();return 'clicked'})()";

    public static string PortFile(UserFolders folders) => Path.Combine(folders.RoamingAppData, "Antigravity", "DevToolsActivePort");

    /// <summary>The debugging port Antigravity wrote at its last start; null when it never ran.</summary>
    public static int? Port(UserFolders folders)
    {
        try { return int.TryParse(File.ReadLines(PortFile(folders)).FirstOrDefault(), out var port) && port is > 0 and < 65536 ? port : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The agent manager's page among the debugging targets (its own origin; the start screen is a data: page).</summary>
    public static string? HubPage(string targets)
    {
        try
        {
            return (JsonNode.Parse(targets) as JsonArray)?.OfType<JsonObject>().FirstOrDefault(t =>
                t["type"]?.GetValue<string>() == "page" && (t["url"]?.GetValue<string>() ?? "").StartsWith("https://127.0.0.1:", StringComparison.Ordinal))?
                ["webSocketDebuggerUrl"]?.GetValue<string>();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Same request, whatever the spaces and line breaks the chat box adds.</summary>
    public static bool SameText(string? shown, string prompt) =>
        shown is not null && string.Concat(shown.Where(c => !char.IsWhiteSpace(c))) == string.Concat(prompt.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// Waits (Antigravity may be starting) for its chat box, then writes the request in it. True only when the box
    /// shows the request; false when it is not there in time or holds something else (the caller falls back to paste).
    /// </summary>
    public static async Task<bool> WriteAsync(string prompt, UserFolders folders, HttpClient http, TimeSpan wait, CancellationToken cancel = default)
    {
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (Port(folders) is { } port && HubPage(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", cancel)) is { } page)
                {
                    using var cdp = await Cdp.OpenAsync(page, cancel);
                    while (DateTime.UtcNow < deadline)
                    {
                        var home = await cdp.EvalAsync(Home, cancel);
                        if (home == "none") return false;
                        var shown = home == "home" ? await cdp.EvalAsync(ComposerText, cancel) : null;
                        if (shown is not null)
                        {
                            if (SameText(shown, prompt)) return true;
                            if (shown.Trim().Length > 0) return false;
                            if (await cdp.EvalAsync(Focus, cancel) != "true") return false;
                            await cdp.CallAsync("Input.insertText", new JsonObject { ["text"] = prompt }, cancel);
                            for (var i = 0; i < 10; i++)
                            {
                                if (SameText(await cdp.EvalAsync(ComposerText, cancel), prompt)) return true;
                                await Task.Delay(100, cancel);
                            }
                            return false;
                        }
                        await Task.Delay(250, cancel);
                    }
                    return false;
                }
            }
            catch (Exception error) when (error is HttpRequestException or WebSocketException or IOException or TaskCanceledException && !cancel.IsCancellationRequested) { }
            await Task.Delay(500, cancel);
        }
        return false;
    }

    /// <summary>The few Chrome DevTools Protocol calls this needs, over the page's WebSocket.</summary>
    sealed class Cdp : IDisposable
    {
        readonly ClientWebSocket socket = new();
        int next;

        public static async Task<Cdp> OpenAsync(string url, CancellationToken cancel)
        {
            var cdp = new Cdp();
            try { await cdp.socket.ConnectAsync(new Uri(url), cancel); }
            catch { cdp.Dispose(); throw; }
            return cdp;
        }

        public async Task<JsonNode?> CallAsync(string method, JsonObject parameters, CancellationToken cancel)
        {
            var id = ++next;
            var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters };
            await socket.SendAsync(Encoding.UTF8.GetBytes(request.ToJsonString()), WebSocketMessageType.Text, true, cancel);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer, cancel);
                    if (part.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Antigravity closed the page.");
                    message.Write(buffer, 0, part.Count);
                }
                while (!part.EndOfMessage);
                if (JsonNode.Parse(message.ToArray()) is JsonObject answer && answer["id"]?.GetValue<int>() == id)
                    return answer["error"] is null ? answer["result"] : throw new IOException(answer["error"]!.ToJsonString());
            }
        }

        /// <summary>A page expression's value as text (null when it is null or undefined).</summary>
        public async Task<string?> EvalAsync(string expression, CancellationToken cancel)
        {
            var result = await CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true }, cancel);
            return result?["result"]?["value"] is { } value ? value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString() : null;
        }

        public void Dispose() => socket.Dispose();
    }
}
