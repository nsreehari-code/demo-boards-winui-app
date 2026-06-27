using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Controls;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Centre surface pane (port of <c>registry/pane/CentrePane.jsx</c>). Resolves the board's visible cards
/// (everything not claimed by the gandalf/truthset rails) and lays them out by <c>layoutStrategy</c>:
/// <list type="bullet">
///   <item><c>infinite-canvas</c> → the shared <see cref="InfiniteCanvasPane"/> (the full pan/zoom graph).</item>
///   <item>anything else (<c>flowing-cards</c>) → a responsive wrap-grid of full-chrome cards.</item>
/// </list>
/// <para>
/// Platform-difference drops: the web wraps the surface in <c>BoardCoordsProvider</c> seeded from
/// <c>initialLayout</c>; in WinUI the canvas layout is store-backed (<c>BoardStore</c> hydrates it from the
/// managed config), so there is no provider and no <c>initialLayout</c> prop. The frontend's CSS
/// responsive grid (<c>row-cols-1/md-2/xl-3</c>) is realised as a fixed-column wrap-grid.
/// </para>
/// </summary>
public sealed class CentrePane : HookComponent<NodeProps>
{
    /// <summary>Columns in the flowing-cards wrap-grid — matches the web's densest breakpoint (xl-3).</summary>
    private const int WrapGridColumns = 3;

    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> spec = Props.Spec;
        string boardId = spec.TryGetValue("boardId", out object? boardIdValue) && boardIdValue is string id ? id : string.Empty;
        string layoutStrategy = spec.TryGetValue("layoutStrategy", out object? layoutValue) && layoutValue is string strategy
            ? strategy
            : "flowing-cards";
        IReadOnlyList<Func<BoardCardState, bool>>? excludeFilters =
            spec.TryGetValue("excludeFilters", out object? excludeValue)
                ? excludeValue as IReadOnlyList<Func<BoardCardState, bool>>
                : null;
        IReadOnlyList<RendererRule>? rendererRules =
            spec.TryGetValue("rendererRules", out object? rulesValue)
                ? rulesValue as IReadOnlyList<RendererRule>
                : null;

        BoardState board = UseBoardState(boardId);

        // The web spreads the excluded Set (card-id iteration order). BoardStore ids are ordinal-sorted,
        // so sorting the excluded set reproduces that deterministic order.
        IReadOnlyList<string> visibleCardIds = board.ExcludedCards(excludeFilters)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();

        if (layoutStrategy == "infinite-canvas")
        {
            return Component<InfiniteCanvasPane, InfiniteCanvasPaneProps>(
                    new InfiniteCanvasPaneProps(
                        boardId,
                        visibleCardIds,
                        board.CardContents,
                        board.DataObjects,
                        rendererRules))
                .Flex(grow: 1);
        }

        return BuildWrapGrid(boardId, visibleCardIds, rendererRules);
    }

    private static Element BuildWrapGrid(string boardId, IReadOnlyList<string> cardIds, IReadOnlyList<RendererRule>? rendererRules)
    {
        var rows = new List<Element>();
        for (int rowStart = 0; rowStart < cardIds.Count; rowStart += WrapGridColumns)
        {
            var cells = new Element[WrapGridColumns];
            for (int column = 0; column < WrapGridColumns; column++)
            {
                int index = rowStart + column;
                cells[column] = index < cardIds.Count
                    ? Component<CardRenderer, CardRendererProps>(
                            new CardRendererProps(boardId, cardIds[index], rendererRules, Chrome: "full"))
                        .Flex(grow: 1)
                    // Empty filler keeps a short final row column-aligned with the rows above it.
                    : Empty().Flex(grow: 1);
            }

            rows.Add(HStack(12, cells));
        }

        return ScrollViewer(VStack(12, rows.ToArray())).Flex(grow: 1);
    }
}
