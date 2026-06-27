using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Ingest card (port of <c>card/ChatCard.jsx</c>, registered as <c>card:ingest</c>) — the conversational
/// intake card. It is simply the standard <see cref="CardChrome"/> wrapping a compact connected chat pane
/// (<c>GandalfChatPane</c> on the web; <see cref="ConnectedChat.Pane"/> here), so the card body is a full
/// send/receive conversation with file attachments. DOM-only wrappers (the <c>board-ingest-card</c>
/// scroll shell div) are dropped — the connected pane owns its own scrolling.
/// </summary>
public sealed class ChatCard : Component<NodeProps>
{
    public override Element Render()
    {
        string boardId = BoardData.Str(Props.Spec, "boardId") ?? string.Empty;
        string cardId = BoardData.Str(Props.Spec, "cardId") ?? string.Empty;
        string chrome = BoardData.Str(Props.Spec, "chrome") ?? "full";
        bool enableResize = BoardData.BoolOr(Props.Spec, "enableResize", false);

        return Component<CardChrome, CardChromeProps>(new CardChromeProps(
            boardId,
            cardId,
            chrome,
            enableResize,
            ConnectedChat.Pane(boardId, cardId, compact: true)));
    }
}
