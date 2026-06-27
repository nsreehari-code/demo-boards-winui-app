using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Controls.Registry;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorInfiniteCanvasProps(
    BoardInfoState BoardInfo,
    BoardSummaryState Summary,
    IReadOnlyList<BoardCard> Cards,
    BoardCanvasLayoutState LayoutState,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<RendererRule>? RendererRules);

/// <summary>
/// Centre-pane canvas host. A thin adapter that projects the board surface props into
/// <see cref="InfiniteCanvasPane"/> — the faithful port of the frontend pane that owns the card graph,
/// the deterministic seed layout, and the wiring onto the shared <see cref="InfiniteCanvas"/> control.
/// (The standalone demo graph this once hosted has been superseded by the real card pipeline.)
/// </summary>
public sealed class ReactorInfiniteCanvasComponent : Component<ReactorInfiniteCanvasProps>
{
    public override Element Render()
    {
        var cardIds = Props.Cards.Select(card => card.Id).ToArray();
        var cardContents = Props.Cards.ToDictionary(card => card.Id, card => card, StringComparer.Ordinal);

        return Component<InfiniteCanvasPane, InfiniteCanvasPaneProps>(
            new InfiniteCanvasPaneProps(
                Props.BoardInfo.BoardId,
                cardIds,
                cardContents,
                Props.DataObjects,
                Props.RendererRules));
    }
}
