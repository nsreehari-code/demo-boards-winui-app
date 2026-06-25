using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        // It owns the canvas's opaque state blob and feeds it back through CanvasState, proving the
        // controlled round-trip. The blob is opaque — the host never inspects it, it just persists
        // whatever comes back. The canvas knows nothing about boards or cards.
        var (canvasState, setCanvasState) = UseState<JsonElement?>(null);
        var (clickCount, setClickCount) = UseState(0);
        var (likes, setLikes) = UseState(12);

        // The node/port/link graph is plain JSON (here a constant, but in practice this is exactly
        // what an agent emits dynamically). InfiniteCanvasGraph.FromJson splits it into the canvas's
        // two aligned props (Nodes + NodePorts) — both opaque descriptors. Only the RenderNode /
        // RenderNodePort callbacks below stay in C#, because rendering is behaviour, not data.

        // RenderNode: opaque node descriptor -> element. Closes over the reactive state (clickCount /
        // likes) so interactive nodes stay live across re-renders. The canvas owns no chrome, so the
        // node renders its own title.
        Element RenderNode(JsonElement node)
        {
            string id = node.GetProperty("id").GetString()!;
            string? title = node.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;

            Element body = id switch
            {
                "welcome" => VStack(8,
                    TextBlock("This surface is rendered by the standalone InfiniteCanvas component.")
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBlock("Drag any node to move it. Ports sit on the node borders and edges connect them — pan, zoom, and watch the minimap.")
                        .Opacity(0.72)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)),

                "counter" => VStack(10,
                    TextBlock($"Clicked {clickCount} time(s)").FontSize(16).Bold(),
                    Button("Click me", () => setClickCount(clickCount + 1)).AccentButton(),
                    Button("Reset", () => setClickCount(0)).SubtleButton()),

                "stats" => VStack(6,
                    BuildStatRow("Nodes", "5"),
                    BuildStatRow("Zoom range", "0.3x – 2.0x"),
                    BuildStatRow("Grid spacing", "28 px"),
                    BuildStatRow("Likes", likes.ToString()),
                    Button("\U0001F44D Like", () => setLikes(likes + 1)).SubtleButton()),

                "palette" => HStack(8,
                    BuildSwatch(Colors.Crimson),
                    BuildSwatch(Colors.SteelBlue),
                    BuildSwatch(Colors.MediumSeaGreen),
                    BuildSwatch(Colors.Goldenrod)),

                "checklist" => VStack(6,
                    BuildBullet("Declarative node map"),
                    BuildBullet("getInitialNodePos seeds x/y/w/h"),
                    BuildBullet("onCanvasStateCommit round-trip"),
                    BuildBullet("Port rails + edge connectors")),

                "badge" => VStack(8,
                    Ellipse()
                        .Fill(new SolidColorBrush(Colors.MediumPurple))
                        .Width(56)
                        .Height(56)
                        .HAlign(HorizontalAlignment.Center),
                    TextBlock("Arbitrary Reactor content").Opacity(0.75).HAlign(HorizontalAlignment.Center)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)),

                _ => TextBlock(id),
            };

            return title is null
                ? body
                : VStack(8, TextBlock(title).Bold().FontSize(13), body);
        }

        return Component<InfiniteCanvas, InfiniteCanvasProps>(
            new InfiniteCanvasProps(
                Nodes: DemoGraph.Nodes,
                RenderNode: RenderNode,
                NodePorts: DemoGraph.NodePorts,
                RenderNodePort: RenderDemoPort,
                GetInitialNodePos: DemoInitialGeometry,
                CanvasState: canvasState,
                OnCanvasStateCommit: blob => setCanvasState(blob),
                Options: new InfiniteCanvasOptions(ShowGrid: true, MiniMap: InfiniteCanvasMiniMapPlacement.BottomRight, ShowZoomControls: true)));
    }

    // The whole node/port/link graph as JSON — the shape an agent would emit dynamically. "nodes" is an
    // array of opaque node descriptors (id + consumer fields like title); "ports" maps a node id to its
    // per-side opaque port descriptors. Size/position are NOT in the node — they are seeded by
    // GetInitialNodePos and then owned by the canvas in its opaque blob. Parsed once into the two props.
    private static readonly InfiniteCanvasGraph DemoGraph = InfiniteCanvasGraph.FromJson(DemoGraphJson);

    private const string DemoGraphJson = """
    {
      "nodes": [
        { "id": "welcome", "title": "Welcome" },
        { "id": "counter", "title": "Interactive button" },
        { "id": "stats", "title": "Stats panel" },
        { "id": "palette", "title": "Colour swatches" },
        { "id": "checklist", "title": "Feature checklist" },
        { "id": "badge", "title": "Shapes + text" }
      ],
      "ports": {
        "welcome": { "bottom": [ { "id": "out:intro", "label": "intro",
          "links": [ { "target": "counter", "port": "in:intro", "label": "intro" } ] } ] },
        "counter": { "top": [ { "id": "in:intro", "label": "intro" } ],
          "bottom": [ { "id": "out:count", "label": "count",
            "links": [ { "target": "stats", "port": "in:count", "label": "count", "animated": true } ] } ] },
        "stats": { "top": [ { "id": "in:count", "label": "count" } ],
          "bottom": [ { "id": "out:data", "label": "data",
            "links": [ { "target": "checklist", "port": "in:data", "label": "data" } ] } ] },
        "palette": { "right": [ { "id": "out:color", "label": "color",
          "links": [ { "target": "badge", "port": "in:color", "label": "color" } ] } ] },
        "checklist": { "top": [ { "id": "in:data", "label": "data" } ] },
        "badge": { "left": [ { "id": "in:color", "label": "color" } ] }
      }
    }
    """;

    // RenderNodePort: opaque port descriptor + context -> pill. Provide ports (out:) get the accent
    // fill, require ports (in:) the neutral fill; the label comes from the port's JSON "label".
    private static Element RenderDemoPort(JsonElement port, InfiniteCanvasPortRenderContext ctx)
    {
        string id = port.GetProperty("id").GetString()!;
        bool provide = id.StartsWith("out:", StringComparison.Ordinal);
        string label = port.TryGetProperty("label", out JsonElement l) && l.ValueKind == JsonValueKind.String
            ? l.GetString() ?? id
            : id;
        return BuildPortPill(label, provide);
    }

    private static Element BuildPortPill(string label, bool provide) =>
        Button(label, () => { })
            .AutomationName($"Canvas port {label}")
            .CornerRadius(10)
            .Padding(8, 2, 8, 2)
            .MinWidth(0)
            .Background(provide
                ? ReactorMainShellComponent.ResolveBrush("AccentFillColorDefaultBrush")
                : ReactorMainShellComponent.ResolveBrush("ControlFillColorDefaultBrush"))
            .WithBorder(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"), 1)
            .Set(button => button.FontSize = 10);

    // GetInitialNodePos: seed full geometry (x, y, w, h) from the opaque descriptor's id. Used only
    // until the canvas captures the node's geometry in its opaque blob (re-seeds if it goes missing).
    private static InfiniteCanvasNodeGeometry DemoInitialGeometry(InfiniteCanvasNodePlacement placement) =>
        placement.Node.GetProperty("id").GetString() switch
        {
            "welcome" => new InfiniteCanvasNodeGeometry(80, 80, 320, 150),
            "counter" => new InfiniteCanvasNodeGeometry(460, 80, 240, 180),
            "stats" => new InfiniteCanvasNodeGeometry(760, 80, 260, 230),
            "palette" => new InfiniteCanvasNodeGeometry(80, 300, 280, 130),
            "checklist" => new InfiniteCanvasNodeGeometry(420, 320, 280, 190),
            "badge" => new InfiniteCanvasNodeGeometry(760, 360, 220, 160),
            _ => new InfiniteCanvasNodeGeometry(80, 80, 240, 140),
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