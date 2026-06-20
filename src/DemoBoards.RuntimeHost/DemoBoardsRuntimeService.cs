using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ClearScript.V8;

namespace DemoBoards.RuntimeHost;

public sealed class DemoBoardsRuntimeService : IAsyncDisposable
{
    private const string AgentfacePrefix = "http://127.0.0.1:43123/";

    private readonly RuntimePaths paths;
    private readonly HostStorageBridge storageBridge;
    private readonly CopilotFoundryInvocationBridge invocationBridge;
    private readonly HostBoardNotifier boardNotifier;
    private readonly V8ScriptEngine engine;
    private readonly SemaphoreSlim engineLock = new(1, 1);
    private readonly HttpListener httpListener;
    private CancellationTokenSource? listenerCts;
    private Task? listenerTask;
    private string? lastBoardSnapshotJson;
    private int boardChangeNotifications;
    private bool started;

    /// <summary>
    /// Raised whenever the published board snapshot changes (UI subscribes and
    /// re-renders). Fires for both shell-initiated and agentface-initiated edits.
    /// </summary>
    public event EventHandler<BoardSnapshot>? BoardSnapshotChanged;

    public DemoBoardsRuntimeService(RuntimePaths? paths = null)
    {
        this.paths = paths ?? RuntimePaths.CreateDefault();
        Directory.CreateDirectory(this.paths.RootDir);

        storageBridge = new HostStorageBridge(this.paths.HostStorageDir);
        invocationBridge = new CopilotFoundryInvocationBridge();
        boardNotifier = new HostBoardNotifier();
        boardNotifier.BoardChanged += () => Interlocked.Increment(ref boardChangeNotifications);
        engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion | V8ScriptEngineFlags.EnableValueTaskPromiseConversion);
        httpListener = new HttpListener();
        httpListener.Prefixes.Add(AgentfacePrefix);

        InitializeEngine();
    }

    public RuntimeStatus GetStatus()
    {
        return new RuntimeStatus(
            started,
            AgentfacePrefix.TrimEnd('/'),
            paths.RootDir,
            paths.HostStorageDir,
            invocationBridge.GetLastInvocationJson(),
            lastBoardSnapshotJson);
    }

    public BoardSnapshot GetBoardSnapshot()
    {
        return BoardSnapshot.Parse(lastBoardSnapshotJson);
    }

    public int BoardChangeNotificationCount => boardChangeNotifications;

    /// <summary>
    /// Re-reads the published snapshot from the long-lived runtime and notifies
    /// subscribers. Does not mutate the board.
    /// </summary>
    public async Task<BoardSnapshot> RefreshAsync()
    {
        string payload = await InvokeJsAsync("winuiBuildSnapshot").ConfigureAwait(false);
        return PublishSnapshot(payload);
    }

    /// <summary>
    /// Adds a card to the live board and re-publishes the snapshot. The card JSON
    /// must match the runtime card shape ({ id, card_data, view }).
    /// </summary>
    public async Task<BoardSnapshot> AddCardAsync(string cardJson)
    {
        string payload = await InvokeJsAsync("winuiAddCard", cardJson).ConfigureAwait(false);
        return PublishSnapshot(payload);
    }

    private BoardSnapshot PublishSnapshot(string payload)
    {
        lastBoardSnapshotJson = payload;
        BoardSnapshot snapshot = BoardSnapshot.Parse(payload);
        BoardSnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    private async Task<string> InvokeJsAsync(string function, params object[] args)
    {
        await engineLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await AwaitJsString(engine.Invoke(function, args)).ConfigureAwait(false);
        }
        finally
        {
            engineLock.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (started) return;

        storageBridge.ResetStorage();
        invocationBridge.Reset();
        await WarmRuntimeAsync().ConfigureAwait(false);

        listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        httpListener.Start();
        listenerTask = Task.Run(() => ListenLoopAsync(listenerCts.Token), listenerCts.Token);
        started = true;
    }

    public async Task StopAsync()
    {
        if (!started) return;

        started = false;
        if (listenerCts is not null)
        {
            listenerCts.Cancel();
        }

        if (httpListener.IsListening)
        {
            httpListener.Stop();
        }

        if (listenerTask is not null)
        {
            try
            {
                await listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }

        listenerCts?.Dispose();
        listenerCts = null;
        listenerTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        httpListener.Close();
        engine.Dispose();
    }

    private void InitializeEngine()
    {
        engine.AddHostObject("HostStorageBridge", storageBridge);
        engine.AddHostObject("HostInvocationBridge", invocationBridge);
        engine.AddHostObject("HostNotifier", boardNotifier);
        engine.Execute("polyfills.js", @"
if (typeof TextEncoder === 'undefined') {
  globalThis.TextEncoder = class TextEncoder {
    encode(text) {
      const utf8 = unescape(encodeURIComponent(String(text)));
      const bytes = new Uint8Array(utf8.length);
      for (let i = 0; i < utf8.length; i += 1) bytes[i] = utf8.charCodeAt(i);
      return bytes;
    }
  };
}
if (typeof TextDecoder === 'undefined') {
  globalThis.TextDecoder = class TextDecoder {
    decode(bytes) {
      let binary = '';
      for (let i = 0; i < bytes.length; i += 1) binary += String.fromCharCode(bytes[i]);
      return decodeURIComponent(escape(binary));
    }
  };
}
if (typeof btoa === 'undefined') {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';
  globalThis.btoa = function (input) {
    let output = '';
    for (let i = 0; i < input.length; i += 3) {
      const a = input.charCodeAt(i);
      const b = i + 1 < input.length ? input.charCodeAt(i + 1) : NaN;
      const c = i + 2 < input.length ? input.charCodeAt(i + 2) : NaN;
      const triplet = (a << 16) | ((isNaN(b) ? 0 : b) << 8) | (isNaN(c) ? 0 : c);
      output += alphabet[(triplet >> 18) & 63];
      output += alphabet[(triplet >> 12) & 63];
      output += isNaN(b) ? '=' : alphabet[(triplet >> 6) & 63];
      output += isNaN(c) ? '=' : alphabet[triplet & 63];
    }
    return output;
  };
}
if (typeof atob === 'undefined') {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';
  globalThis.atob = function (input) {
    let sanitized = String(input).replace(/=+$/, '');
    let output = '';
    let buffer = 0;
    let bits = 0;
    for (let i = 0; i < sanitized.length; i += 1) {
      const value = alphabet.indexOf(sanitized.charAt(i));
      if (value < 0 || value > 63) continue;
      buffer = (buffer << 6) | value;
      bits += 6;
      if (bits >= 8) {
        bits -= 8;
        output += String.fromCharCode((buffer >> bits) & 255);
      }
    }
    return output;
  };
}
");

        var baseDir = AppContext.BaseDirectory;
        var jsDir = Path.Combine(baseDir, "js");
        engine.Execute("compute-jsonata.js", File.ReadAllText(Path.Combine(jsDir, "compute-jsonata.js")));
        engine.Execute("golden-driver.js", File.ReadAllText(Path.Combine(jsDir, "golden-driver.js")));
        engine.Execute("server-runtime-controlface.js", File.ReadAllText(Path.Combine(jsDir, "server-runtime-controlface.js")));
        engine.Execute("producer-driver.js", File.ReadAllText(Path.Combine(jsDir, "producer-driver.js")));
    }

    private async Task WarmRuntimeAsync()
    {
        const string cardsJson = "["
            + "{\"id\":\"welcome-card\",\"card_data\":{\"title\":\"Demo Boards Runtime\",\"body\":\"WinUI host is running the embedded V8 brain.\",\"host\":\"embedded-v8\"},\"view\":{\"elements\":[]}},"
            + "{\"id\":\"runtime-status-card\",\"card_data\":{\"title\":\"Mounted Adapters\",\"storage\":\"KV / Journal / Queue / Blob\",\"surface\":\"agentface\"},\"view\":{\"elements\":[]}},"
            + "{\"id\":\"metrics-card\",\"card_data\":{"
            + "\"title\":\"Quarter Metrics\",\"revenue\":4250.5,\"status_note\":\"On track for Q3 close.\","
            + "\"tasks\":[\"Draft report\",\"Review numbers\",\"Publish summary\"]},"
            + "\"view\":{\"elements\":["
            + "{\"kind\":\"metric\",\"label\":\"Revenue\",\"data\":{\"bind\":\"card_data.revenue\"}},"
            + "{\"kind\":\"narrative\",\"data\":{\"bind\":\"card_data.status_note\"}},"
            + "{\"kind\":\"list\",\"label\":\"Tasks\",\"data\":{\"bind\":\"card_data.tasks\"}}"
            + "]}},"
            + "{\"id\":\"insights-card\",\"card_data\":{"
            + "\"title\":\"Insights\","
            + "\"summary\":\"# Pipeline\\n**Throughput** is up this week.\\n- Ingest healthy\\n- Review on track\","
            + "\"series\":[{\"label\":\"Mon\",\"value\":12},{\"label\":\"Tue\",\"value\":19},{\"label\":\"Wed\",\"value\":8},{\"label\":\"Thu\",\"value\":24}]},"
            + "\"view\":{\"elements\":["
            + "{\"kind\":\"markdown\",\"data\":{\"bind\":\"card_data.summary\"}},"
            + "{\"kind\":\"chart\",\"label\":\"Daily volume\",\"data\":{\"bind\":\"card_data.series\"}}"
            + "]}}"
            + "]";
        lastBoardSnapshotJson = await InvokeJsAsync("initWinuiRuntime", "winui-board", cardsJson).ConfigureAwait(false);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await httpListener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var pathAndQuery = context.Request.Url?.PathAndQuery ?? "/";
            var method = context.Request.HttpMethod;

            if (method == "GET" && path == "/healthz")
            {
                await WriteJsonAsync(context.Response, 200, "{\"status\":\"ok\"}").ConfigureAwait(false);
                return;
            }

            if (method == "GET" && (path == "/mcp" || path == "/mcp-raw"))
            {
                await WriteJsonAsync(context.Response, 200, "{\"status\":\"ok\",\"surface\":\"agentface\",\"transport\":\"localhost-http\",\"post\":\"" + path + "\"}").ConfigureAwait(false);
                return;
            }

            // Convenience MCP routes map onto the runtime's real agentface paths.
            if (method == "POST" && (path == "/mcp" || path == "/mcp-raw"))
            {
                string body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                await ProxyToRuntimeApiAsync(context, method, "/api/board" + path, body).ConfigureAwait(false);
                return;
            }

            // Full runtime API surface (MCP, SSE one-shot, card files, …) proxied
            // verbatim into the long-lived runtime — real semantics, no stubs.
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                string body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                await ProxyToRuntimeApiAsync(context, method, pathAndQuery, body).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && path == "/board/cards")
            {
                string body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(body))
                {
                    await WriteJsonAsync(context.Response, 400, "{\"error\":\"card body required\"}").ConfigureAwait(false);
                    return;
                }

                BoardSnapshot snapshot = await AddCardAsync(body).ConfigureAwait(false);
                var response = "{\"status\":\"accepted\",\"surface\":\"agentface\",\"cardCount\":" + snapshot.CardCount + "}";
                await WriteJsonAsync(context.Response, 200, response).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context.Response, 404, "{\"error\":\"not-found\"}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, 500, "{\"error\":\"" + EscapeJson(ex.Message) + "\"}").ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Forwards an HTTP request into the runtime's handleRuntimeApi via the
    /// embedded JS bridge and relays the runtime's status + body back to the
    /// caller. Mutating verbs trigger a snapshot re-publish so the UI stays live.
    /// </summary>
    private async Task ProxyToRuntimeApiAsync(HttpListenerContext context, string method, string pathAndQuery, string body)
    {
        string raw = await InvokeJsAsync("winuiHandleRuntimeApi", method, pathAndQuery, body ?? string.Empty).ConfigureAwait(false);

        int status = 200;
        string responseBody = raw;
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.Number)
            {
                status = statusElement.GetInt32();
            }
            if (root.TryGetProperty("body", out JsonElement bodyElement) && bodyElement.ValueKind == JsonValueKind.String)
            {
                responseBody = bodyElement.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Relay the raw JS return as-is if it is not the {status, body} envelope.
        }

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshAsync().ConfigureAwait(false);
        }

        await WriteJsonAsync(context.Response, status, string.IsNullOrEmpty(responseBody) ? "{}" : responseBody).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string payload)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(payload);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private static string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static async Task<string> AwaitJsString(object value)
    {
        if (value is Task<string> stringTask) return await stringTask.ConfigureAwait(false);
        if (value is Task<object> objectTask) return (string)(await objectTask.ConfigureAwait(false));
        if (value is string text) return text;
        throw new InvalidOperationException($"Unexpected JS return type: {value?.GetType().FullName ?? "<null>"}");
    }
}

/// <summary>
/// In-process board-change seam. The embedded runtime calls this when the board
/// mutates (watchers/SSE collapse to a direct host call in the desktop model).
/// </summary>
public sealed class HostBoardNotifier
{
    public event Action? BoardChanged;

    public void NotifyBoardChanged()
    {
        BoardChanged?.Invoke();
    }
}