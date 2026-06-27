using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DemoBoards.RuntimeHost;

public sealed class DemoBoardsRuntimeService : IAsyncDisposable
{
    private const int PreferredAgentfacePort = 43123;
    private const int DynamicPortAttemptCount = 8;

    private readonly RuntimePaths paths;
    private readonly HostStorageBridge storageBridge;
    private readonly HostControlfaceBridge controlfaceBridge;
    private readonly HostBoardNotifier boardNotifier;
    private readonly RuntimeJsHost jsHost;
    private readonly BoardSseStateReducerHost boardSseStateReducerHost;
    private readonly RuntimeHttpRequestProcessor requestProcessor;
    private HostInvocationBridge invocationBridge;
    private HttpListener httpListener;
    private CancellationTokenSource? listenerCts;
    private Task? listenerTask;
    private string agentfacePrefix;
    private int boardChangeNotifications;
    private bool started;
    private BoardSseCanonicalEnvelope canonicalEnvelope = BoardSseCanonicalEnvelope.Empty;
    private string? lastPublishedPayloadJson;
    private readonly object notificationReductionGate = new();
    private Task notificationReductionTail = Task.CompletedTask;

    public event EventHandler<BoardSseCanonicalEnvelope>? BoardCanonicalStateChanged;

    public DemoBoardsRuntimeService(RuntimePaths? paths = null)
    {
        this.paths = paths ?? RuntimePaths.CreateDefault();
        Directory.CreateDirectory(this.paths.RootDir);

        storageBridge = new HostStorageBridge(this.paths.HostStorageDir);
        controlfaceBridge = new HostControlfaceBridge(this.paths.RootDir, AppContext.BaseDirectory);
        boardNotifier = new HostBoardNotifier();
        boardNotifier.BoardChanged += () => Interlocked.Increment(ref boardChangeNotifications);
        boardNotifier.BoardNotificationsReceived += HandleBoardNotificationsReceived;
        agentfacePrefix = BuildAgentfacePrefix(PreferredAgentfacePort);
        invocationBridge = CreateInvocationBridge(agentfacePrefix);
        jsHost = new RuntimeJsHost(storageBridge, controlfaceBridge, invocationBridge, boardNotifier);
        boardSseStateReducerHost = new BoardSseStateReducerHost(jsHost);
        httpListener = CreateHttpListener(agentfacePrefix);
        requestProcessor = new RuntimeHttpRequestProcessor(ProxyRuntimeApiAsync, AddCardAsync);
    }

    public RuntimeStatus GetStatus()
    {
        return new RuntimeStatus(
            started,
            agentfacePrefix.TrimEnd('/'),
            paths.RootDir,
            paths.HostStorageDir,
            invocationBridge.GetLastInvocationJson(),
                lastPublishedPayloadJson);
    }

    public BoardSseCanonicalEnvelope GetBoardCanonicalState()
    {
        return canonicalEnvelope;
    }

    public int BoardChangeNotificationCount => boardChangeNotifications;

    /// <summary>
    /// Re-reads the published snapshot from the long-lived runtime and notifies
    /// subscribers. Does not mutate the board.
    /// </summary>
    public async Task RefreshAsync()
    {
        string payload = await jsHost.InvokeJsAsync("winuiBuildSnapshot").ConfigureAwait(false);
        string reducedPayload = await boardSseStateReducerHost.ReplacePublishedPayloadAsync(payload).ConfigureAwait(false);
        lastPublishedPayloadJson = reducedPayload;
        await PublishCanonicalEnvelopeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a card to the live board and re-publishes the snapshot. The card JSON
    /// must match the runtime card shape ({ id, card_data, view }).
    /// </summary>
    public async Task AddCardAsync(string cardJson)
    {
        string payload = await jsHost.InvokeJsAsync("winuiAddCard", cardJson).ConfigureAwait(false);
        string reducedPayload = await boardSseStateReducerHost.ReplacePublishedPayloadAsync(payload).ConfigureAwait(false);
        lastPublishedPayloadJson = reducedPayload;
        await PublishCanonicalEnvelopeAsync().ConfigureAwait(false);
    }

    public async Task<(int StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)> ProxyRuntimeApiAsync(string method, string path, string? bodyJson = null, IReadOnlyDictionary<string, string>? requestHeaders = null)
    {
        string requestHeadersJson = JsonSerializer.Serialize(requestHeaders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        string payload = await jsHost.InvokeJsAsync("winuiHandleRuntimeApi", method, path, bodyJson ?? string.Empty, requestHeadersJson).ConfigureAwait(false);
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

    private void HandleBoardNotificationsReceived(string notificationsJson)
    {
        string normalizedNotifications = notificationsJson ?? "[]";
        lock (notificationReductionGate)
        {
            notificationReductionTail = notificationReductionTail
                .ContinueWith(
                    _ => ApplyNotificationsAndPublishAsync(normalizedNotifications),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();
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
        jsHost.Dispose();
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

    private HostInvocationBridge CreateInvocationBridge(string prefix)
    {
        return new HostInvocationBridge(controlfaceBridge, prefix.TrimEnd('/'));
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

    private async Task WarmRuntimeAsync()
    {
        const string cardsJson = "["
            + "{\"id\":\"welcome-card\",\"card_data\":{\"title\":\"Demo Boards Runtime\",\"body\":\"WinUI host is running the embedded V8 brain.\",\"host\":\"embedded-v8\"},\"view\":{\"elements\":[]}},"
            + "{\"id\":\"runtime-status-card\",\"card_data\":{\"title\":\"Mounted Adapters\",\"storage\":\"KV / Journal / Queue / Blob\",\"surface\":\"agentface\"},\"view\":{\"elements\":[]}},"
            + "{\"id\":\"gandalf-ingest-card\",\"card_data\":{\"title\":\"Gandalf Ingest\",\"mode\":\"ingest\",\"owner\":\"board-manager\",\"requires\":[\"source.report\"],\"provides\":[{\"bindTo\":\"report.summary\"}]},\"source_defs\":[{\"bindTo\":\"source.report\",\"url\":\"https://example.invalid/report.json\",\"kind\":\"http\",\"timeout\":30000}],\"view\":{\"elements\":[{\"kind\":\"ingest\"},{\"kind\":\"markdown\"}]}}"
            + "]";
        string payload = await jsHost.InvokeJsAsync("initWinuiRuntime", "winui-board", cardsJson).ConfigureAwait(false);
        string reducedPayload = await boardSseStateReducerHost.InitializeFromPublishedPayloadAsync(payload).ConfigureAwait(false);
        lastPublishedPayloadJson = reducedPayload;
        await PublishCanonicalEnvelopeAsync().ConfigureAwait(false);
    }

    private void PublishCanonicalEnvelope()
    {
        canonicalEnvelope = BoardSseCanonicalEnvelope.Parse(boardSseStateReducerHost.GetCanonicalEnvelopeAsync().GetAwaiter().GetResult());
        BoardCanonicalStateChanged?.Invoke(this, canonicalEnvelope);
    }

    private async Task PublishCanonicalEnvelopeAsync()
    {
        canonicalEnvelope = BoardSseCanonicalEnvelope.Parse(await boardSseStateReducerHost.GetCanonicalEnvelopeAsync().ConfigureAwait(false));
        BoardCanonicalStateChanged?.Invoke(this, canonicalEnvelope);
    }

    private async Task ApplyNotificationsAndPublishAsync(string notificationsJson)
    {
        string reducedPayload = await boardSseStateReducerHost.ApplyNotificationsAsync(notificationsJson).ConfigureAwait(false);
        lastPublishedPayloadJson = reducedPayload;
        await PublishCanonicalEnvelopeAsync().ConfigureAwait(false);
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

            _ = Task.Run(() => requestProcessor.HandleRequestAsync(context), cancellationToken);
        }
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