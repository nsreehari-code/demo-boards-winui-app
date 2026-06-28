using System.Threading.Tasks;

namespace DemoBoards_WinUI.SseReducer;

internal sealed class BoardSseStateReducerHost
{
    private readonly ReducerJsHost jsHost;

    public BoardSseStateReducerHost(ReducerJsHost jsHost)
    {
        this.jsHost = jsHost;
    }

    public Task<string> InitializeFromPublishedPayloadAsync(string publishedPayloadJson)
    {
        return jsHost.InvokeJsAsync("winuiBoardSseReducerInitFromPublishedPayload", publishedPayloadJson);
    }

    public Task<string> ReplacePublishedPayloadAsync(string publishedPayloadJson)
    {
        return jsHost.InvokeJsAsync("winuiBoardSseReducerReplacePublishedPayload", publishedPayloadJson);
    }

    public Task<string> ApplyNotificationsAsync(string notificationsJson)
    {
        return jsHost.InvokeJsAsync("winuiBoardSseReducerApplyNotifications", notificationsJson);
    }

    public Task<string> GetCanonicalEnvelopeAsync()
    {
        return jsHost.InvokeJsAsync("winuiBoardSseReducerGetCanonicalEnvelope");
    }
}