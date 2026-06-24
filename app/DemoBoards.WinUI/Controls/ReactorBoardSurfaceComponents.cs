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
        // DEMO HOST for the independent, declarative InfiniteCanvas component.
        // It owns the layout (positions + viewport) as state and feeds it back through
        // SavedLayout, proving the controlled round-trip. The canvas itself knows nothing
        // about boards or cards — it just renders whatever nodes it is handed.
        var (layout, setLayout) = UseState<InfiniteCanvasLayout?>(null);
        var (clickCount, setClickCount) = UseState(0);
        var (likes, setLikes) = UseState(12);

        var nodes = new Dictionary<string, InfiniteCanvasNode>(StringComparer.Ordinal)
        {
            ["welcome"] = new InfiniteCanvasNode(
                VStack(8,
                    TextBlock("This surface is rendered by the standalone InfiniteCanvas component.")
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBlock("Drag any node to move it. Pan with scrollbars, zoom with the controls, and watch the minimap.")
                        .Opacity(0.72)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)),
                Width: 320,
                Height: 150,
                Title: "Welcome"),

            ["counter"] = new InfiniteCanvasNode(
                VStack(10,
                    TextBlock($"Clicked {clickCount} time(s)").FontSize(16).Bold(),
                    Button("Click me", () => setClickCount(clickCount + 1)).AccentButton(),
                    Button("Reset", () => setClickCount(0)).SubtleButton()),
                Width: 240,
                Height: 180,
                Title: "Interactive button"),

            ["stats"] = new InfiniteCanvasNode(
                VStack(6,
                    BuildStatRow("Nodes", "5"),
                    BuildStatRow("Zoom range", "0.3x – 2.0x"),
                    BuildStatRow("Grid spacing", "28 px"),
                    BuildStatRow("Likes", likes.ToString()),
                    Button("\U0001F44D Like", () => setLikes(likes + 1)).SubtleButton()),
                Width: 260,
                Height: 230,
                Title: "Stats panel"),

            ["palette"] = new InfiniteCanvasNode(
                HStack(8,
                    BuildSwatch(Colors.Crimson),
                    BuildSwatch(Colors.SteelBlue),
                    BuildSwatch(Colors.MediumSeaGreen),
                    BuildSwatch(Colors.Goldenrod)),
                Width: 280,
                Height: 130,
                Title: "Colour swatches"),

            ["checklist"] = new InfiniteCanvasNode(
                VStack(6,
                    BuildBullet("Declarative node map"),
                    BuildBullet("getInitialPosition fallback"),
                    BuildBullet("onLayoutChange round-trip"),
                    BuildBullet("Internal zoom + minimap")),
                Width: 280,
                Height: 190,
                Title: "Feature checklist"),

            ["badge"] = new InfiniteCanvasNode(
                VStack(8,
                    Ellipse()
                        .Fill(new SolidColorBrush(Colors.MediumPurple))
                        .Width(56)
                        .Height(56)
                        .HAlign(HorizontalAlignment.Center),
                    TextBlock("Arbitrary Reactor content").Opacity(0.75).HAlign(HorizontalAlignment.Center)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)),
                Width: 220,
                Height: 160,
                Title: "Shapes + text"),
        };

        return Component<InfiniteCanvas, InfiniteCanvasProps>(
            new InfiniteCanvasProps(
                Nodes: nodes,
                SavedLayout: layout,
                GetInitialPosition: DemoInitialPosition,
                OnLayoutChange: setLayout,
                Options: new InfiniteCanvasOptions(ShowGrid: true, MiniMap: InfiniteCanvasMiniMapPlacement.BottomRight, ShowZoomControls: true)));
    }

    private static InfiniteCanvasNodePosition DemoInitialPosition(string id) => id switch
    {
        "welcome" => new InfiniteCanvasNodePosition(80, 80),
        "counter" => new InfiniteCanvasNodePosition(460, 80),
        "stats" => new InfiniteCanvasNodePosition(760, 80),
        "palette" => new InfiniteCanvasNodePosition(80, 300),
        "checklist" => new InfiniteCanvasNodePosition(420, 320),
        "badge" => new InfiniteCanvasNodePosition(760, 360),
        _ => new InfiniteCanvasNodePosition(80, 80),
    };

    private static Element BuildStatRow(string label, string value) =>
        HStack(8,
            TextBlock(label).Opacity(0.7).Flex(grow: 1),
            TextBlock(value).Bold());

    private static Element BuildSwatch(Windows.UI.Color color) =>
        Border(Rectangle().Fill(new SolidColorBrush(color)).Width(48).Height(48))
            .CornerRadius(8);

    private static Element BuildBullet(string text) =>
        HStack(8,
            Ellipse().Fill(new SolidColorBrush(Colors.MediumSeaGreen)).Width(8).Height(8),
            TextBlock(text).Opacity(0.85));
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