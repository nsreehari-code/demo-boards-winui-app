using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoBoards_WinUI.SseReducer;

namespace DemoBoards_WinUI.State;

public sealed class HostedBoardStateService : IAsyncDisposable
{
    private readonly HttpClient httpClient;
    private readonly ReducerJsHost reducerJsHost;
    private readonly BoardSseStateReducerHost reducerHost;
    private readonly SemaphoreSlim reducerGate = new(1, 1);
    private readonly CancellationTokenSource disposeCts = new();
    private bool initialized;
    private Task? streamTask;
    private BoardSseCanonicalEnvelope currentEnvelope = BoardSseCanonicalEnvelope.Empty;

    public HostedBoardStateService(string serverUrl, string boardId)
    {
        BoardId = NormalizeRequired(boardId, "Board id is required.");
        string normalizedServerUrl = NormalizeServerUrl(serverUrl);
        ServerBaseUri = new Uri(normalizedServerUrl + "/", UriKind.Absolute);
        ClientId = Guid.NewGuid().ToString("D");
        httpClient = new HttpClient { BaseAddress = ServerBaseUri };
        reducerJsHost = new ReducerJsHost();
        reducerHost = new BoardSseStateReducerHost(reducerJsHost);
    }

    public string BoardId { get; }

    public string ClientId { get; }

    public Uri ServerBaseUri { get; }

    public BoardSseCanonicalEnvelope CurrentCanonicalState => currentEnvelope;

    public event EventHandler<BoardSseCanonicalEnvelope>? BoardCanonicalStateChanged;

    public async Task StartAsync()
    {
        await RefreshSnapshotAsync().ConfigureAwait(false);
        streamTask = Task.Run(() => RunSseLoopAsync(disposeCts.Token));
    }

    public async Task RefreshSnapshotAsync()
    {
        using var response = await httpClient.GetAsync(GetBoardApiPath("sse?one-shot"), disposeCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string rawPayload = await response.Content.ReadAsStringAsync(disposeCts.Token).ConfigureAwait(false);
        string publishedPayloadJson = ExtractFirstSseDataPayload(rawPayload);
        await ReplacePublishedPayloadAsync(publishedPayloadJson).ConfigureAwait(false);
    }

    public async Task ResetRuntimeFromSeedCardsAsync()
    {
        using var response = await httpClient.PostAsync(GetBoardApiPath("reset-runtime-from-seed-cards"), content: null, disposeCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadAsStringAsync(disposeCts.Token).ConfigureAwait(false);
        await RefreshSnapshotAsync().ConfigureAwait(false);
    }

    private async Task RunSseLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, GetBoardApiPath($"sse?clientId={Uri.EscapeDataString(ClientId)}"));
                using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var payloadBuilder = new StringBuilder();

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        if (payloadBuilder.Length > 0)
                        {
                            string payload = payloadBuilder.ToString();
                            payloadBuilder.Clear();
                            await ApplyNotificationPayloadAsync(payload).ConfigureAwait(false);
                        }

                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        string dataLine = line.Length > 5 ? line[5..] : string.Empty;
                        if (dataLine.StartsWith(" ", StringComparison.Ordinal))
                        {
                            dataLine = dataLine[1..];
                        }

                        if (payloadBuilder.Length > 0)
                        {
                            payloadBuilder.Append('\n');
                        }

                        payloadBuilder.Append(dataLine);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ReplacePublishedPayloadAsync(string publishedPayloadJson)
    {
        BoardSseCanonicalEnvelope nextEnvelope;
        await reducerGate.WaitAsync(disposeCts.Token).ConfigureAwait(false);
        try
        {
            if (!initialized)
            {
                await reducerHost.InitializeFromPublishedPayloadAsync(publishedPayloadJson).ConfigureAwait(false);
                initialized = true;
            }
            else
            {
                await reducerHost.ReplacePublishedPayloadAsync(publishedPayloadJson).ConfigureAwait(false);
            }

            nextEnvelope = BoardSseCanonicalEnvelope.Parse(await reducerHost.GetCanonicalEnvelopeAsync().ConfigureAwait(false));
            currentEnvelope = nextEnvelope;
        }
        finally
        {
            reducerGate.Release();
        }

        BoardCanonicalStateChanged?.Invoke(this, nextEnvelope);
    }

    private async Task ApplyNotificationPayloadAsync(string payloadJson)
    {
        BoardSseCanonicalEnvelope nextEnvelope;
        await reducerGate.WaitAsync(disposeCts.Token).ConfigureAwait(false);
        try
        {
            if (!initialized)
            {
                await reducerHost.InitializeFromPublishedPayloadAsync(payloadJson).ConfigureAwait(false);
                initialized = true;
            }
            else
            {
                await reducerHost.ApplyNotificationsAsync(payloadJson).ConfigureAwait(false);
            }

            nextEnvelope = BoardSseCanonicalEnvelope.Parse(await reducerHost.GetCanonicalEnvelopeAsync().ConfigureAwait(false));
            currentEnvelope = nextEnvelope;
        }
        finally
        {
            reducerGate.Release();
        }

        BoardCanonicalStateChanged?.Invoke(this, nextEnvelope);
    }

    private string GetBoardApiPath(string suffix)
    {
        return $"api/boards/{Uri.EscapeDataString(BoardId)}/{suffix}";
    }

    private static string ExtractFirstSseDataPayload(string rawSse)
    {
        using var reader = new StringReader(rawSse ?? string.Empty);
        var payloadBuilder = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                if (payloadBuilder.Length > 0)
                {
                    return payloadBuilder.ToString();
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            string dataLine = line.Length > 5 ? line[5..] : string.Empty;
            if (dataLine.StartsWith(" ", StringComparison.Ordinal))
            {
                dataLine = dataLine[1..];
            }

            if (payloadBuilder.Length > 0)
            {
                payloadBuilder.Append('\n');
            }

            payloadBuilder.Append(dataLine);
        }

        if (payloadBuilder.Length == 0)
        {
            throw new InvalidOperationException("sse one-shot bootstrap missing data frame");
        }

        return payloadBuilder.ToString();
    }

    private static string NormalizeRequired(string? value, string message)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(message);
        }

        return normalized;
    }

    private static string NormalizeServerUrl(string? serverUrl)
    {
        string normalized = NormalizeRequired(serverUrl, "Server URL is required.").TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Server URL must be an absolute http or https URL.");
        }

        return normalized;
    }

    public async ValueTask DisposeAsync()
    {
        disposeCts.Cancel();
        if (streamTask is not null)
        {
            try
            {
                await streamTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        httpClient.Dispose();
        reducerGate.Dispose();
        disposeCts.Dispose();
        reducerJsHost.Dispose();
    }
}