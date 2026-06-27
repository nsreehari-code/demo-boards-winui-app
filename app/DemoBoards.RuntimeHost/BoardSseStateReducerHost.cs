using System.Threading.Tasks;

namespace DemoBoards.RuntimeHost;

internal sealed class BoardSseStateReducerHost
{
    private readonly RuntimeJsHost jsHost;

    public BoardSseStateReducerHost(RuntimeJsHost jsHost)
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
