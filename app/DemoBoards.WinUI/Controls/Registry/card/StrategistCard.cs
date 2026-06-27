using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Strategist card (port of <c>StrategistCard.jsx</c>): the same body as the default card — a
/// <see cref="CardviewRenderer"/> view-tree inside the shared <see cref="CardChrome"/>. Registered under
/// <c>card:strategist</c> so renderer rules can route strategist cards independently; chrome behaviour is
/// identical to <see cref="CardShell"/>.
/// </summary>
public sealed class StrategistCard : Component<NodeProps>
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
