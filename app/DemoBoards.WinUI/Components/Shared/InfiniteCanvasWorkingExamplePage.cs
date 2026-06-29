using System;
using System.Collections.Generic;
using System.Text.Json;
using DemoBoards_WinUI;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Hooks;

namespace DemoBoards_WinUI.Controls.Shared;

public static class BoardTestAppModes
{
    public const string Default = "default";
    public const string InfiniteCanvasWorkingExample = "infinite-canvas-working-example";
}

public sealed record InfiniteCanvasWorkingExamplePageProps();

public sealed class InfiniteCanvasWorkingExamplePage : HookComponent<InfiniteCanvasWorkingExamplePageProps>
{
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

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (canvasState, setCanvasState) = UseState<JsonElement?>(null);
        var (clickCount, setClickCount) = UseState(0);
        var (likes, setLikes) = UseState(12);

        Element RenderNode(JsonElement node)
        {
            string id = node.GetProperty("id").GetString()!;
            string? title = node.TryGetProperty("title", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

            Element body = id switch
            {
                "welcome" => VStack(8,
                    TextBlock("This page replays the historical InfiniteCanvas demo graph.")
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBlock("Use it to verify visible ports, derived edges, pan, zoom, and minimap behavior independent of the live board graph.")
                        .Opacity(0.72)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)),

                "counter" => VStack(10,
                    TextBlock($"Clicked {clickCount} time(s)").FontSize(16).Bold(),
                    Button("Click me", () => setClickCount(clickCount + 1)).AccentButton(),
                    Button("Reset", () => setClickCount(0)).SubtleButton()),

                "stats" => VStack(6,
                    BuildStatRow(theme, "Nodes", "6"),
                    BuildStatRow(theme, "Zoom range", "0.24x - 1.35x"),
                    BuildStatRow(theme, "Grid spacing", "24 px"),
                    BuildStatRow(theme, "Likes", likes.ToString()),
                    Button("Like", () => setLikes(likes + 1)).SubtleButton()),

                "palette" => HStack(8,
                    BuildSwatch(Colors.Crimson),
                    BuildSwatch(Colors.SteelBlue),
                    BuildSwatch(Colors.MediumSeaGreen),
                    BuildSwatch(Colors.Goldenrod)),

                "checklist" => VStack(6,
                    BuildBullet(theme, "Declarative node map"),
                    BuildBullet(theme, "GetInitialNodePos seeds x, y, w, h"),
                    BuildBullet(theme, "OnCanvasStateCommit round-trips the opaque blob"),
                    BuildBullet(theme, "Port rails and derived edge connectors")),

                "badge" => VStack(8,
                    Ellipse()
                        .Fill(new SolidColorBrush(Colors.MediumPurple))
                        .Width(56)
                        .Height(56)
                        .HAlign(HorizontalAlignment.Center),
                    TextBlock("Arbitrary Reactor content")
                        .Opacity(0.75)
                        .HAlign(HorizontalAlignment.Center)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)),

                _ => TextBlock(id),
            };

            return Border(
                    VStack(10,
                        title is null
                            ? Empty()
                            : TextBlock(title).Bold().FontSize(13),
                        body))
                .Padding(14)
                .Background(theme.CardBackground)
                .CornerRadius(14)
                .WithBorder(theme.CardBorderStrong, 1);
        }

        Element RenderDemoPort(JsonElement port, InfiniteCanvasPortRenderContext ctx)
        {
            string id = port.GetProperty("id").GetString()!;
            bool provide = id.StartsWith("out:", StringComparison.Ordinal);
            string label = port.TryGetProperty("label", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? id
                : id;

            return Button(label, () => { })
                .AutomationName($"Canvas port {label}")
                .CornerRadius(10)
                .Padding(8, 2, 8, 2)
                .MinWidth(0)
                .Background(provide ? theme.Accent : theme.ControlFill)
                .Foreground(provide ? theme.TextOnAccent : theme.TextPrimary)
                .WithBorder(theme.CardBorder, 1)
                .Set(button => button.FontSize = 10);
        }

        InfiniteCanvasNodeGeometry? GetInitialNodePos(InfiniteCanvasNodePlacement placement) =>
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

        return VStack(12,
                Border(
                    VStack(4,
                        TextBlock("InfiniteCanvas Working Example").FontSize(16).Bold(),
                        TextBlock("Historical demo graph for validating visible ports and derived edges before debugging live board topology.")
                            .Opacity(0.72)
                            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)))
                    .Padding(12)
                    .Background(theme.SecondaryCardBackground)
                    .CornerRadius(12)
                    .WithBorder(theme.CardBorder, 1),
                Component<InfiniteCanvas, InfiniteCanvasProps>(
                    new InfiniteCanvasProps(
                        Nodes: DemoGraph.Nodes,
                        RenderNode: RenderNode,
                        NodePorts: DemoGraph.NodePorts,
                        RenderNodePort: RenderDemoPort,
                        GetInitialNodePos: GetInitialNodePos,
                        CanvasState: canvasState,
                        OnCanvasStateCommit: blob => setCanvasState(blob),
                        StateKey: "infinite-canvas-working-example",
                        Options: new InfiniteCanvasOptions(
                            MinZoom: 0.24,
                            MaxZoom: 1.35,
                            ShowGrid: true,
                            MiniMap: InfiniteCanvasMiniMapPlacement.BottomRight,
                            ShowZoomControls: true,
                            ShowEdgeLabels: true,
                            GridSpacing: 24)))
                    .Flex(grow: 1))
            .Padding(12)
            .Flex(grow: 1);
    }

    private static Element BuildStatRow(AppTheme theme, string label, string value) =>
        HStack(8,
            TextBlock(label).Opacity(0.72).Foreground(theme.TextMuted).Flex(grow: 1),
            TextBlock(value).Bold());

    private static Element BuildSwatch(Color color) =>
        Border(Rectangle().Fill(new SolidColorBrush(color)).Width(48).Height(48))
            .CornerRadius(8);

    private static Element BuildBullet(AppTheme theme, string text) =>
        HStack(8,
            Ellipse().Fill(theme.Accent).Width(8).Height(8),
            TextBlock(text).Set(block => block.TextWrapping = TextWrapping.WrapWholeWords));
}