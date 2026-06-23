using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ClearScript.V8;

namespace DemoBoards.RuntimeHost;

public sealed class DemoBoardsRuntimeService : IAsyncDisposable
{
    private const int PreferredAgentfacePort = 43123;
    private const int DynamicPortAttemptCount = 8;

    private readonly RuntimePaths paths;
    private readonly HostStorageBridge storageBridge;
    private readonly HostControlfaceBridge controlfaceBridge;
    private readonly HostBoardNotifier boardNotifier;
    private readonly V8ScriptEngine engine;
    private readonly SemaphoreSlim engineLock = new(1, 1);
    private CopilotFoundryInvocationBridge invocationBridge;
    private HttpListener httpListener;
    private CancellationTokenSource? listenerCts;
    private Task? listenerTask;
    private string agentfacePrefix;
    private string? lastBoardSnapshotJson;
    private int boardChangeNotifications;
    private bool started;

    /// <summary>
    /// Raised whenever the published board snapshot changes (UI subscribes and
    /// re-renders). Fires for both shell-initiated and agentface-initiated edits.
    /// </summary>
    public event EventHandler<BoardSnapshot>? BoardSnapshotChanged;
    public event EventHandler<string>? RuntimeNotificationsReceived;

    public DemoBoardsRuntimeService(RuntimePaths? paths = null)
    {
        this.paths = paths ?? RuntimePaths.CreateDefault();
        Directory.CreateDirectory(this.paths.RootDir);

        storageBridge = new HostStorageBridge(this.paths.HostStorageDir);
        controlfaceBridge = new HostControlfaceBridge(this.paths.RootDir, AppContext.BaseDirectory);
        boardNotifier = new HostBoardNotifier();
        boardNotifier.BoardChanged += () => Interlocked.Increment(ref boardChangeNotifications);
        boardNotifier.BoardNotificationsReceived += HandleBoardNotificationsReceived;
        engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion | V8ScriptEngineFlags.EnableValueTaskPromiseConversion);
        agentfacePrefix = BuildAgentfacePrefix(PreferredAgentfacePort);
        invocationBridge = CreateInvocationBridge(agentfacePrefix);
        httpListener = CreateHttpListener(agentfacePrefix);

        InitializeEngine();
    }

    public RuntimeStatus GetStatus()
    {
        return new RuntimeStatus(
            started,
            agentfacePrefix.TrimEnd('/'),
            paths.RootDir,
            paths.HostStorageDir,
            invocationBridge.GetLastInvocationJson(),
            lastBoardSnapshotJson);
    }

    public BoardSnapshot GetBoardSnapshot()
    {
        return BoardSnapshot.Parse(lastBoardSnapshotJson);
    }

    public IReadOnlyDictionary<string, BoardWatchpartyState> GetCardWatchparties()
    {
        return BoardSnapshot.ParseWatchparties(lastBoardSnapshotJson);
    }

    public IReadOnlyDictionary<string, BoardWatchpartyState> GetCardWatchparties(string agentOutputChannel, string agentToolsChannel)
    {
        return BoardSnapshot.ParseWatchparties(lastBoardSnapshotJson, agentOutputChannel, agentToolsChannel);
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

    public async Task<(int StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)> ProxyRuntimeApiAsync(string method, string path, string? bodyJson = null, IReadOnlyDictionary<string, string>? requestHeaders = null)
    {
        string requestHeadersJson = JsonSerializer.Serialize(requestHeaders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        string payload = await InvokeJsAsync("winuiHandleRuntimeApi", method, path, bodyJson ?? string.Empty, requestHeadersJson).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        int statusCode = root.TryGetProperty("statusCode", out JsonElement statusElement)
            && statusElement.ValueKind == JsonValueKind.Number
            ? statusElement.GetInt32()
            : 200;
        string body = root.TryGetProperty("body", out JsonElement bodyElement)
            && bodyElement.ValueKind == JsonValueKind.String
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("headers", out JsonElement headersElement)
            && headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in headersElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    headers[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshAsync().ConfigureAwait(false);
        }

        return (statusCode, body, headers);
    }

    private BoardSnapshot PublishSnapshot(string payload)
    {
        lastBoardSnapshotJson = payload;
        BoardSnapshot snapshot = BoardSnapshot.Parse(payload);
        BoardSnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    private void HandleBoardNotificationsReceived(string notificationsJson)
    {
        RuntimeNotificationsReceived?.Invoke(this, notificationsJson);
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
        listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            StartHttpListenerWithFallback();
            await WarmRuntimeAsync().ConfigureAwait(false);
            listenerTask = Task.Run(() => ListenLoopAsync(listenerCts.Token), listenerCts.Token);
            started = true;
        }
        catch
        {
            if (httpListener.IsListening)
            {
                httpListener.Stop();
            }

            listenerCts.Dispose();
            listenerCts = null;
            listenerTask = null;
            throw;
        }
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

    private void StartHttpListenerWithFallback()
    {
        try
        {
            httpListener.Start();
            return;
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 183)
        {
        }

        Exception? lastError = null;
        for (int attempt = 0; attempt < DynamicPortAttemptCount; attempt += 1)
        {
            string fallbackPrefix = BuildAgentfacePrefix(AllocateDynamicLoopbackPort());
            RebindAgentfaceEndpoint(fallbackPrefix);

            try
            {
                httpListener.Start();
                return;
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 183)
            {
                lastError = ex;
            }
        }

        throw new HttpListenerException(183, lastError?.Message ?? $"Failed to bind the embedded agentface listener after {DynamicPortAttemptCount} fallback attempts.");
    }

    private void RebindAgentfaceEndpoint(string prefix)
    {
        httpListener.Close();
        agentfacePrefix = prefix;
        invocationBridge.UpdateServerUrl(prefix);
        httpListener = CreateHttpListener(prefix);
    }

    private CopilotFoundryInvocationBridge CreateInvocationBridge(string prefix)
    {
        return new CopilotFoundryInvocationBridge(controlfaceBridge, prefix.TrimEnd('/'));
    }

    private static HttpListener CreateHttpListener(string prefix)
    {
        HttpListener listener = new();
        listener.Prefixes.Add(prefix);
        return listener;
    }

    private static string BuildAgentfacePrefix(int port)
    {
        return $"http://127.0.0.1:{port}/";
    }

    private static int AllocateDynamicLoopbackPort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private void InitializeEngine()
    {
        engine.AddHostObject("HostStorageBridge", storageBridge);
        engine.AddHostObject("HostControlfaceBridge", controlfaceBridge);
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
        engine.Execute("controlface-embedded-shared.js", File.ReadAllText(Path.Combine(jsDir, "controlface-embedded-shared.js")));
        engine.Execute("agentface-embedded-shared.js", File.ReadAllText(Path.Combine(jsDir, "agentface-embedded-shared.js")));
        engine.Execute("server-runtime-controlface.js", File.ReadAllText(Path.Combine(jsDir, "server-runtime-controlface.js")));
        engine.Execute("producer-driver.js", File.ReadAllText(Path.Combine(jsDir, "producer-driver.js")));
    }

    private async Task WarmRuntimeAsync()
    {
        const string cardsJson = "["
            + "{\"id\":\"welcome-card\",\"card_data\":{\"title\":\"Demo Boards Runtime\",\"body\":\"WinUI host is running the embedded V8 brain.\",\"host\":\"embedded-v8\"},\"view\":{\"elements\":[]}},"
            + "{\"id\":\"runtime-status-card\",\"card_data\":{\"title\":\"Mounted Adapters\",\"storage\":\"KV / Journal / Queue / Blob\",\"surface\":\"agentface\"},\"view\":{\"elements\":[]}},"
            + "{\"id\":\"gandalf-ingest-card\",\"card_data\":{\"title\":\"Gandalf Ingest\",\"mode\":\"ingest\",\"owner\":\"board-manager\",\"requires\":[\"source.report\"],\"provides\":[{\"bindTo\":\"report.summary\"}]},\"source_defs\":[{\"bindTo\":\"source.report\",\"url\":\"https://example.invalid/report.json\",\"kind\":\"http\",\"timeout\":30000}],\"view\":{\"elements\":[{\"kind\":\"ingest\"},{\"kind\":\"markdown\"}]}}"
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
            var pathAndQuery = context.Request.Url?.PathAndQuery ?? path;
            if (context.Request.HttpMethod == "GET" && path == "/healthz")
            {
                await WriteJsonAsync(context.Response, 200, "{\"status\":\"ok\"}").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && (path == "/mcp" || path == "/mcp-raw"))
            {
                await WriteJsonAsync(context.Response, 200, "{\"status\":\"ok\",\"surface\":\"agentface\",\"transport\":\"localhost-http\"}").ConfigureAwait(false);
                return;
            }

            if (ShouldProxyRequest(path, context.Request.HttpMethod))
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Dictionary<string, string> requestHeaders = new(StringComparer.OrdinalIgnoreCase);
                foreach (string? key in context.Request.Headers.AllKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        requestHeaders[key] = context.Request.Headers[key] ?? string.Empty;
                    }
                }

                (int statusCode, string responseBody, IReadOnlyDictionary<string, string> headers) = await ProxyRuntimeApiAsync(context.Request.HttpMethod, pathAndQuery, body, requestHeaders).ConfigureAwait(false);
                await WriteResponseAsync(context.Response, statusCode, string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody, headers).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/board/cards")
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

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

    private static bool ShouldProxyRequest(string path, string? httpMethod)
    {
        string method = httpMethod?.ToUpperInvariant() ?? "GET";
        if (path.StartsWith("/api/boards/", StringComparison.Ordinal))
        {
            return method is "GET" or "POST" or "PATCH" or "PUT" or "DELETE";
        }

        return path is "/mcp"
            or "/mcp-raw"
            or "/mcp-actions"
            or "/mcp-controlplane"
            or "/mcp-webhooks"
            or "/mcp-extras"
            or "/manage-boards"
            or "/agent/mcp"
            or "/agent/mcp/manifest";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string payload)
    {
        await WriteResponseAsync(response, statusCode, payload, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["content-type"] = "application/json; charset=utf-8"
        }).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, int statusCode, string payload, IReadOnlyDictionary<string, string>? headers)
    {
        response.StatusCode = statusCode;

        string contentType = "application/json; charset=utf-8";
        if (headers is not null)
        {
            foreach ((string key, string value) in headers)
            {
                if (string.Equals(key, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = value;
                }
                else if (!string.Equals(key, "content-length", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    response.Headers[key] = value;
                }
            }
        }

        response.ContentType = contentType;
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
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
    public event Action<string>? BoardNotificationsReceived;

    public void NotifyBoardChanged()
    {
        BoardChanged?.Invoke();
    }

    public void NotifyBoardNotifications(string notificationsJson)
    {
        BoardNotificationsReceived?.Invoke(notificationsJson ?? "[]");
    }
}