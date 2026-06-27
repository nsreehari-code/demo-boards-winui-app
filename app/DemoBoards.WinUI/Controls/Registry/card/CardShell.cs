using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Default card (port of <c>CardShell.jsx</c>): the standard card body — a <see cref="CardviewRenderer"/>
/// view-tree — inside the shared <see cref="CardChrome"/>. All chrome lives in <see cref="CardChrome"/>;
/// this entry only supplies the body. <c>boardId</c>/<c>cardId</c>/<c>enableResize</c>/<c>chrome</c> are
/// read from the node <c>spec</c> the engine passes.
/// </summary>
public sealed class CardShell : Component<NodeProps>
{
    public override Element Render()
    {
        string boardId = BoardData.Str(Props.Spec, "boardId") ?? string.Empty;
        string cardId = BoardData.Str(Props.Spec, "cardId") ?? string.Empty;
        bool enableResize = BoardData.BoolOr(Props.Spec, "enableResize", false);
        string chrome = BoardData.Str(Props.Spec, "chrome") ?? "full";

        return Component<CardChrome, CardChromeProps>(new CardChromeProps(
            boardId,
            cardId,
            chrome,
            enableResize,
            Component<CardviewRenderer, CardviewRendererProps>(new CardviewRendererProps(boardId, cardId))));
    }
}
