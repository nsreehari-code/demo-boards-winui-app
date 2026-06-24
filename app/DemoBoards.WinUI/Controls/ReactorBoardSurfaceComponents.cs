using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public static class ReactorBoardSurfaceModes
{
    public const string InfiniteCanvas = "infinite-canvas";
    public const string CardsFlow = "flowing-cards";
}

public sealed record ReactorInfiniteCanvasProps(
    BoardInfoState BoardInfo,
    BoardSummaryState Summary,
    IReadOnlyList<BoardCard> Cards,
    BoardCanvasLayoutState LayoutState,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<RendererRule>? RendererRules);

public sealed class ReactorInfiniteCanvasComponent : Component<ReactorInfiniteCanvasProps>
{
    public override Element Render()
    {
        return Component<ReactorBoardCanvasComponent, ReactorBoardCanvasProps>(
                new ReactorBoardCanvasProps(
                    Props.BoardInfo,
                    Props.Summary,
                    Props.Cards,
                    Props.LayoutState,
                    Props.DataObjects,
                    Props.RendererRules))
            .Flex(grow: 1);
    }
}

public sealed record ReactorCardsFlowProps(
    BoardInfoState BoardInfo,
    BoardSummaryState Summary,
    IReadOnlyList<BoardCard> Cards,
    BoardCanvasLayoutState LayoutState,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<RendererRule>? RendererRules);

public sealed class ReactorCardsFlowComponent : Component<ReactorCardsFlowProps>
{
    public override Element Render()
    {
        Element[] rows = ChunkCards(Props.Cards, 2)
            .Select((row, rowIndex) =>
                (Element)HStack(12,
                        row.Select(card => BuildFlowCard(card))
                            .Concat(row.Count == 1
                                ? new[]
                                {
                                    Rectangle()
                                        .Fill(new SolidColorBrush(Colors.Transparent))
                                        .Flex(grow: 1)
                                }
                                : Array.Empty<Element>())
                            .ToArray())
                    .WithKey($"flow-row-{rowIndex}"))
            .ToArray();

        Element content = rows.Length == 0
            ? Border(TextBlock("No centre cards available").Opacity(0.6))
                .Padding(20)
                .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorSecondaryBrush"))
                .CornerRadius(14)
            : VStack(12, rows.ToArray());

        return Border(
                ScrollViewer(content)
                    .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto)
                    .Set(scrollViewer => scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled))
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorSecondaryBrush"))
            .CornerRadius(14)
            .Padding(8)
            .Flex(grow: 1);
    }

    private Element BuildFlowCard(BoardCard card)
    {
        return Border(
                Component<ReactorCardRendererComponent, ReactorCardRendererProps>(
                    new ReactorCardRendererProps(card, Props.RendererRules)))
            .Padding(2)
            .Background(new SolidColorBrush(Colors.Transparent))
            .WithBorder(CardToneBrushes.CreateToneBrush(card.Status, 0x44), 1)
            .CornerRadius(18)
            .Flex(grow: 1)
            .MinHeight(220)
            .WithKey($"flow-card-{card.Id}");
    }

    private static IReadOnlyList<IReadOnlyList<BoardCard>> ChunkCards(IReadOnlyList<BoardCard> cards, int chunkSize)
    {
        if (cards.Count == 0)
        {
            return Array.Empty<IReadOnlyList<BoardCard>>();
        }

        var rows = new List<IReadOnlyList<BoardCard>>();
        for (int index = 0; index < cards.Count; index += chunkSize)
        {
            rows.Add(cards.Skip(index).Take(chunkSize).ToArray());
        }

        return rows;
    }
}