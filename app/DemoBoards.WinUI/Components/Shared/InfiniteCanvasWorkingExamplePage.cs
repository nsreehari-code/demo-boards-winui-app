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
using DemoBoards_WinUI.Controls.Registry;

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
                { "id": "badge", "title": "Shapes + text" },
                { "id": "metricKind", "title": "CardView: metric" },
                { "id": "narrativeKind", "title": "CardView: narrative" },
                { "id": "todoKind", "title": "CardView: todo" },
                { "id": "editableTableKind", "title": "CardView: editable-table" },
                { "id": "formKind", "title": "CardView: form" },
                { "id": "chartKind", "title": "CardView: chart" },
                { "id": "tableKind", "title": "CardView: table" },
                { "id": "listKind", "title": "CardView: list" },
                { "id": "alertKind", "title": "CardView: alert" },
                { "id": "badgeViewKind", "title": "CardView: badge" },
                { "id": "textKind", "title": "CardView: text" },
                { "id": "actionsKind", "title": "CardView: actions" },
                { "id": "selectionKind", "title": "CardView: selection" },
                { "id": "searchboxKind", "title": "CardView: searchbox" },
                { "id": "notesKind", "title": "CardView: notes" },
                { "id": "markdownKind", "title": "CardView: markdown" },
                { "id": "uploadKind", "title": "CardView: multi-file-upload" }
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
                "checklist": { "top": [ { "id": "in:data", "label": "data" } ],
                    "bottom": [ { "id": "out:roadmap", "label": "roadmap",
                        "links": [ { "target": "todoKind", "port": "in:roadmap", "label": "roadmap" } ] } ] },
                "badge": { "left": [ { "id": "in:color", "label": "color" } ],
                    "right": [ { "id": "out:status", "label": "status",
                        "links": [ { "target": "metricKind", "port": "in:status", "label": "status" } ] } ] },
                "metricKind": { "left": [ { "id": "in:status", "label": "status" } ],
                    "bottom": [ { "id": "out:summary", "label": "summary",
                        "links": [ { "target": "narrativeKind", "port": "in:summary", "label": "summary" } ] } ] },
                "narrativeKind": { "top": [ { "id": "in:summary", "label": "summary" } ] },
                "todoKind": { "top": [ { "id": "in:roadmap", "label": "roadmap" } ],
                    "right": [ { "id": "out:plan", "label": "plan",
                        "links": [ { "target": "editableTableKind", "port": "in:plan", "label": "plan" } ] } ] },
                "editableTableKind": { "left": [ { "id": "in:plan", "label": "plan" } ],
                    "right": [ { "id": "out:details", "label": "details",
                        "links": [ { "target": "formKind", "port": "in:details", "label": "details" } ] } ] },
                "formKind": { "left": [ { "id": "in:details", "label": "details" } ],
                    "bottom": [ { "id": "out:trend", "label": "trend",
                        "links": [ { "target": "chartKind", "port": "in:trend", "label": "trend" } ] } ] },
                "chartKind": { "top": [ { "id": "in:trend", "label": "trend" } ],
                    "right": [ { "id": "out:rows", "label": "rows",
                        "links": [ { "target": "tableKind", "port": "in:rows", "label": "rows" } ] } ] },
                "tableKind": { "left": [ { "id": "in:rows", "label": "rows" } ],
                    "bottom": [ { "id": "out:list", "label": "list",
                        "links": [ { "target": "listKind", "port": "in:list", "label": "list" } ] } ] },
                "listKind": { "top": [ { "id": "in:list", "label": "list" } ] }
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
                    BuildStatRow(theme, "Nodes", DemoGraph.Nodes.Count.ToString()),
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

                "metricKind" => Component<MetricKind, NodeProps>(MetricKindSampleProps()),

                "narrativeKind" => Component<NarrativeKind, NodeProps>(NarrativeKindSampleProps()),

                "todoKind" => Component<TodoKind, NodeProps>(TodoKindSampleProps()),

                "editableTableKind" => Component<EditableTableKind, NodeProps>(EditableTableKindSampleProps()),

                "formKind" => Component<FormKind, NodeProps>(FormKindSampleProps()),

                "chartKind" => Component<ChartKind, NodeProps>(ChartKindSampleProps()),

                "tableKind" => Component<TableKind, NodeProps>(TableKindSampleProps()),

                "listKind" => Component<ListKind, NodeProps>(ListKindSampleProps()),

                "alertKind" => Component<AlertKind, NodeProps>(AlertKindSampleProps()),

                "badgeViewKind" => Component<BadgeKind, NodeProps>(BadgeKindSampleProps()),

                "textKind" => Component<TextKind, NodeProps>(TextKindSampleProps()),

                "actionsKind" => Component<ActionsKind, NodeProps>(ActionsKindSampleProps()),

                "selectionKind" => Component<SelectionKind, NodeProps>(SelectionKindSampleProps()),

                "searchboxKind" => Component<SearchboxKind, NodeProps>(SearchboxKindSampleProps()),

                "notesKind" => Component<NotesKind, NodeProps>(NotesKindSampleProps()),

                "markdownKind" => Component<MarkdownKind, NodeProps>(MarkdownKindSampleProps()),

                "uploadKind" => Component<MultiFileUploadKind, NodeProps>(MultiFileUploadKindSampleProps()),

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

            return Button(string.Empty, () => { })
                .AutomationName($"Canvas port {label}")
                .CornerRadius(999)
                .Padding(0)
                .MinWidth(0)
                .MinHeight(0)
                .Width(1)
                .Height(1)
                .Background(provide ? theme.Accent : theme.ControlFill)
                .WithBorder(provide ? theme.Accent : theme.ControlFill, 1)
                .Set(button => button.Foreground = theme.Transparent);
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
                "metricKind" => new InfiniteCanvasNodeGeometry(1060, 100, 250, 110),
                "narrativeKind" => new InfiniteCanvasNodeGeometry(1040, 260, 320, 140),
                "todoKind" => new InfiniteCanvasNodeGeometry(80, 520, 300, 180),
                "editableTableKind" => new InfiniteCanvasNodeGeometry(430, 560, 430, 210),
                "formKind" => new InfiniteCanvasNodeGeometry(920, 520, 320, 220),
                "chartKind" => new InfiniteCanvasNodeGeometry(1280, 520, 380, 270),
                "tableKind" => new InfiniteCanvasNodeGeometry(1710, 540, 340, 200),
                "listKind" => new InfiniteCanvasNodeGeometry(80, 820, 300, 170),
                "alertKind" => new InfiniteCanvasNodeGeometry(430, 820, 240, 110),
                "badgeViewKind" => new InfiniteCanvasNodeGeometry(710, 820, 220, 110),
                "textKind" => new InfiniteCanvasNodeGeometry(970, 820, 330, 120),
                "actionsKind" => new InfiniteCanvasNodeGeometry(1340, 820, 340, 120),
                "selectionKind" => new InfiniteCanvasNodeGeometry(1720, 820, 280, 120),
                "searchboxKind" => new InfiniteCanvasNodeGeometry(80, 1040, 320, 120),
                "notesKind" => new InfiniteCanvasNodeGeometry(450, 1010, 420, 260),
                "markdownKind" => new InfiniteCanvasNodeGeometry(920, 1010, 420, 220),
                "uploadKind" => new InfiniteCanvasNodeGeometry(1390, 1010, 430, 240),
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

    private static NodeProps SampleNodeProps(
        IReadOnlyDictionary<string, object?>? spec = null,
        string? label = null,
        object? data = null,
        object? currentValue = null,
        string? writeTo = null)
        => new(
            Spec: spec ?? new Dictionary<string, object?>(StringComparer.Ordinal),
            Meta: new NodeMeta(Label: label),
            Variant: null,
            Data: data,
            CurrentValue: currentValue,
            WriteTo: writeTo,
            OnSave: null,
            Status: null,
            Services: null,
            Children: null);

    private static NodeProps MetricKindSampleProps()
        => SampleNodeProps(
            label: "Investigation coverage",
            data: 87);

    private static NodeProps NarrativeKindSampleProps()
        => SampleNodeProps(
            data: "Coverage is strong across identity and public footprint, but supporting evidence still needs one concise summary for handoff.");

    private static NodeProps TodoKindSampleProps()
        => SampleNodeProps(
            data: new IReadOnlyDictionary<string, object?>[]
            {
                Map(("text", "Validate company identity"), ("done", true)),
                Map(("text", "Collect supporting documents"), ("done", false)),
                Map(("text", "Draft final summary"), ("done", false)),
            });

    private static NodeProps EditableTableKindSampleProps()
        => SampleNodeProps(
            spec: Map(
                ("schema", Map(
                    ("properties", Map(
                        ("workstream", Map(("title", "Workstream"))),
                        ("owner", Map(("title", "Owner"))),
                        ("eta_days", Map(("title", "ETA"), ("type", "number"))))))),
                ("columns", new object?[] { "workstream", "owner", "eta_days" }),
                ("addRow", true),
                ("deleteRow", true),
                ("placeholder", "No actions queued")),
            data: new IReadOnlyDictionary<string, object?>[]
            {
                Map(("workstream", "Identity review"), ("owner", "Analyst"), ("eta_days", 2d)),
                Map(("workstream", "Evidence pack"), ("owner", "Research"), ("eta_days", 3d)),
            });

    private static NodeProps FormKindSampleProps()
        => SampleNodeProps(
            spec: Map(
                ("fields", Map(
                    ("properties", Map(
                        ("subject", Map(("type", "string"), ("title", "Subject"))),
                        ("priority", Map(("type", "string"), ("title", "Priority"), ("enum", new object?[] { "high", "medium", "low" }))),
                        ("confirmed", Map(("type", "boolean"), ("title", "Confirmed"))))),
                    ("required", new object?[] { "subject" }))),
                ("saveLabel", "Apply")),
            data: Map(("subject", "Alex Rivera"), ("priority", "high"), ("confirmed", false)));

    private static NodeProps ChartKindSampleProps()
        => new(
            Spec: Map(("height", 220d)),
            Meta: new NodeMeta(Label: "Signal trend"),
            Variant: "bar",
            Data: new List<object?>
            {
                Map(("label", "Mon"), ("value", 3d)),
                Map(("label", "Tue"), ("value", 5d)),
                Map(("label", "Wed"), ("value", 4d)),
                Map(("label", "Thu"), ("value", 7d)),
                Map(("label", "Fri"), ("value", 6d)),
            },
            CurrentValue: null,
            WriteTo: null,
            OnSave: null,
            Status: null,
            Services: null,
            Children: null);

    private static NodeProps TableKindSampleProps()
        => SampleNodeProps(
            data: new List<object?>
            {
                Map(("source", "HRIS"), ("status", "verified"), ("score", 0.96d)),
                Map(("source", "LinkedIn"), ("status", "pending"), ("score", 0.81d)),
                Map(("source", "Press"), ("status", "captured"), ("score", 0.74d)),
            });

    private static NodeProps ListKindSampleProps()
        => SampleNodeProps(
            data: new List<object?> { "Identity confirmed", "Risk review in progress", "Summary pending sign-off" });

    private static NodeProps AlertKindSampleProps()
        => SampleNodeProps(
            spec: Map(("thresholds", Map(("green", "<= 3"), ("amber", "<= 6")))),
            label: "Open risks",
            data: 5d);

    private static NodeProps BadgeKindSampleProps()
        => SampleNodeProps(
            spec: Map(("colorMap", Map(("verified", "success"), ("pending", "warning"), ("blocked", "danger")))),
            data: "verified");

    private static NodeProps TextKindSampleProps()
        => SampleNodeProps(
            spec: Map(("style", "muted-italic")),
            data: "Evidence collected from internal and public sources is ready for synthesis.");

    private static NodeProps ActionsKindSampleProps()
        => SampleNodeProps(
            spec: Map(("buttons", new object?[]
            {
                Map(("id", "save"), ("label", "Save")),
                Map(("id", "refresh"), ("label", "Refresh")),
                Map(("id", "archive"), ("label", "Archive")),
            })),
            label: "Available actions");

    private static NodeProps SelectionKindSampleProps()
        => SampleNodeProps(
            spec: Map(("fields", Map(
                ("properties", Map(("status", Map(("title", "Status"), ("enum", new object?[] { "open", "review", "closed" }))))),
                ("required", new object?[] { "status" })))),
            currentValue: "review");

    private static NodeProps SearchboxKindSampleProps()
        => SampleNodeProps(
            spec: Map(
                ("fields", Map(("properties", Map(("query", Map(("title", "Search subject"))))))),
                ("actionLabel", "Find")),
            currentValue: "Alex Rivera");

    private static NodeProps NotesKindSampleProps()
        => SampleNodeProps(
            data: "- Confirmed employer history\n- Need one more source for public footprint\n- Final summary due tomorrow");

    private static NodeProps MarkdownKindSampleProps()
        => SampleNodeProps(
            data: "## Brief\n\n- Identity matched across systems\n- Evidence pack assembled\n- Awaiting final reviewer sign-off");

    private static NodeProps MultiFileUploadKindSampleProps()
        => SampleNodeProps(
            spec: Map(("placeholder", "Add an evidence note..."), ("submitLabel", "Upload evidence")),
            data: Map(
                ("files", new object?[]
                {
                    Map(("name", "resume.pdf"), ("stored_name", "resume.pdf"), ("size", 152340d)),
                    Map(("name", "org-chart.png"), ("stored_name", "org-chart.png"), ("size", 48320d)),
                }),
                ("filegroups", new object?[]
                {
                    Map(("message", "Initial evidence bundle"), ("file_idxs", new object?[] { 0, 1 }))
                })));

    private static IReadOnlyDictionary<string, object?> Map(params (string Key, object? Value)[] entries)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, object? value) in entries)
        {
            map[key] = value;
        }

        return map;
    }
}