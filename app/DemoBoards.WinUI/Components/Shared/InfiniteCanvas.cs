using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Shared;

// =====================================================================================
//  InfiniteCanvas — an independent, declarative pan/zoom surface for arbitrary Reactor
//  components. It is a *controlled* component aligned to the frontend on seven props:
//
//    • Nodes              : list of opaque node descriptors. WHAT to render.
//    • NodePorts          : opaque per-side port descriptors per node.
//    • RenderNode         : (node) => element.
//    • RenderNodePort     : (port, { side, node }) => element.
//    • GetInitialNodePos  : (descriptor, { index, nodeCount, nodes, placed }) => { x, y, w, h }.
//                           Seeds geometry for any node not present in CanvasState. WinUI needs a
//                           concrete box, so (unlike the frontend) it also returns width/height.
//    • CanvasState        : a single OPAQUE blob the canvas owns. It seeds geometry/viewport from it
//                           and stores back whatever it needs (positions, sizes, viewport, …). The
//                           consumer round-trips it verbatim and never inspects it.
//    • OnCanvasStateCommit: fired with the new opaque blob whenever geometry/viewport changes.
//
//  Guarantee: anything the canvas needs to restore itself lives in the opaque blob; if a node is
//  missing (or its blob entry is unusable) the canvas re-seeds it from GetInitialNodePos.
//  The canvas owns viewport + presentation chrome only (grid, minimap, zoom controls).
//  It knows nothing about boards or cards.
//
//  Beyond the seven, two non-core knobs (NOT part of the frozen contract):
//    • StateKey : changing it re-seeds the whole canvas (discards live drags + viewport) — used when
//                 the host swaps to a logically different graph. Mirrors the frontend `stateKey`.
//    • Options  : zoom limits, grid, minimap placement, zoom controls, grid spacing. Any future
//                 presentation/interaction knob goes HERE rather than as a new bespoke prop.
//  Edges are NOT a prop: the topology is declared via port "links" and the canvas derives the edge
//  layer internally (intentional divergence from the frontend's explicit `edges` prop).
// =====================================================================================

// A node "descriptor" is an OPAQUE backend object (a JSON value). The canvas reads only "id"; size is
// NOT a node concern (any "width" on a node is a consumer-owned semantic hint the canvas ignores).
// Geometry comes from GetInitialNodePos (seed) and the opaque CanvasState blob (persisted actuals).
// This mirrors the frontend `nodes: [{ id, ...viewState }]`, so the SAME backend data drives either stack.

/// <summary>World-space position of a node (top-left).</summary>
public sealed record InfiniteCanvasNodePosition(double X, double Y);

/// <summary>Which border of a node a port rail is attached to.</summary>
public enum InfiniteCanvasPortSide
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>
/// Per-side port rails for one node. Each rail is a list of OPAQUE port descriptors (JSON values).
/// The canvas reads only each port's "id" (and the optional "links" it uses to derive the edge
/// layer); the whole descriptor is handed to <c>RenderNodePort</c>. Mirrors the frontend
/// <c>nodePorts[id] = { top?, bottom?, left?, right? }</c>.
/// </summary>
public sealed record InfiniteCanvasNodePorts(
    IReadOnlyList<JsonElement>? Top = null,
    IReadOnlyList<JsonElement>? Bottom = null,
    IReadOnlyList<JsonElement>? Left = null,
    IReadOnlyList<JsonElement>? Right = null);

/// <summary>
/// Placement context for <c>GetInitialNodePos</c> — mirrors the frontend
/// <c>getInitialNodePos(descriptor, { index, nodeCount, nodes, placed })</c>. <see cref="Placed"/>
/// is the running map of already-placed node positions.
/// </summary>
public sealed record InfiniteCanvasNodePlacement(
    JsonElement Node,
    int Index,
    int NodeCount,
    IReadOnlyList<JsonElement> Nodes,
    IReadOnlyDictionary<string, InfiniteCanvasNodePosition> Placed);

/// <summary>
/// The seed geometry <c>GetInitialNodePos</c> returns for a node — top-left position plus the box
/// size. The frontend only seeds <c>{ x, y }</c> (ReactFlow auto-measures); WinUI needs a concrete
/// box, so it also seeds <see cref="Width"/> / <see cref="Height"/>. The seed is only used until the
/// node's geometry is captured in the opaque CanvasState blob.
/// </summary>
public sealed record InfiniteCanvasNodeGeometry(double X, double Y, double Width, double Height);

/// <summary>
/// Render context for <c>RenderNodePort</c> — mirrors the frontend
/// <c>renderNodePort(port, { side, node })</c>: the rail <see cref="Side"/> and the owning opaque
/// node descriptor.
/// </summary>
public sealed record InfiniteCanvasPortRenderContext(
    InfiniteCanvasPortSide Side,
    JsonElement Node);

/// <summary>
/// Internal, derived representation of a connector. The canvas builds these from the optional
/// "links" on the opaque port descriptors — edges are NOT a public prop; the topology lives inside
/// the node/port declaration. When <see cref="SourcePort"/> / <see cref="TargetPort"/> resolve they
/// anchor the curve to that port; otherwise it falls back to source bottom-centre / target top-centre.
/// </summary>
internal sealed record InfiniteCanvasEdge(
    string Id,
    string Source,
    string Target,
    string? SourcePort = null,
    string? TargetPort = null,
    string? Label = null,
    bool Animated = false);

/// <summary>
/// Internal: the canvas's resolved view of one opaque node descriptor — its <see cref="Id"/>, the
/// concrete layout box (<see cref="Width"/> / <see cref="Height"/>) the WinUI canvas needs to place
/// it, and the original opaque <see cref="Descriptor"/> handed back to <c>RenderNode</c>.
/// </summary>
internal sealed record InfiniteCanvasNodeBox(
    string Id,
    double Width,
    double Height,
    JsonElement Descriptor);

/// <summary>Internal: pan (content offset in view px) + zoom factor, persisted inside the opaque blob.</summary>
internal sealed record InfiniteCanvasViewport(double OffsetX, double OffsetY, double Zoom);

public enum InfiniteCanvasMiniMapPlacement
{
    Off,
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
}

public sealed record InfiniteCanvasOptions(
    double MinZoom = 0.3,
    double MaxZoom = 2.0,
    bool ShowGrid = true,
    InfiniteCanvasMiniMapPlacement MiniMap = InfiniteCanvasMiniMapPlacement.TopRight,
    bool ShowZoomControls = true,
    double ContentPadding = 240,
    double GridSpacing = 28);

/// <summary>
/// The InfiniteCanvas public contract.
/// <para>
/// ⚠️ FROZEN CONTRACT — the seven core props below (<c>Nodes</c>, <c>NodePorts</c>, <c>RenderNode</c>,
/// <c>RenderNodePort</c>, <c>GetInitialNodePos</c>, <c>CanvasState</c>, <c>OnCanvasStateCommit</c>) are
/// deliberately aligned 1:1 with the frontend InfiniteCanvas. Their names, shapes, and semantics MUST
/// NOT change — neither renamed, retyped, reordered, removed, nor have their behaviour altered —
/// without EXPLICIT, DOUBLE user approval (the user must confirm twice). Do not "improve" or refactor
/// them speculatively. <c>Options</c> is the only non-core prop and may evolve more freely.
/// </para>
/// </summary>
public sealed record InfiniteCanvasProps(
    // --- BEGIN FROZEN SEVEN-PROP CONTRACT (do not change without explicit double user approval) ---
    IReadOnlyList<JsonElement> Nodes,
    Func<JsonElement, Element> RenderNode,
    IReadOnlyDictionary<string, InfiniteCanvasNodePorts>? NodePorts = null,
    Func<JsonElement, InfiniteCanvasPortRenderContext, Element>? RenderNodePort = null,
    Func<InfiniteCanvasNodePlacement, InfiniteCanvasNodeGeometry?>? GetInitialNodePos = null,
    JsonElement? CanvasState = null,
    Action<JsonElement>? OnCanvasStateCommit = null,
    // --- END FROZEN SEVEN-PROP CONTRACT ---
    string? StateKey = null,
    InfiniteCanvasOptions? Options = null);

/// <summary>
/// A convenience parser that splits one backend graph JSON into the two aligned props
/// (<c>Nodes</c> + <c>NodePorts</c>). This is the entry point for <b>dynamically created</b> graphs —
/// e.g. emitted by an agent as JSON. Nodes and ports stay OPAQUE: the canvas reads only the structural
/// fields it needs (node "id"/"width"/"height", port "id"/"links"); every other field is preserved as
/// a cloned <see cref="JsonElement"/> for the host's <c>RenderNode</c> / <c>RenderNodePort</c> callbacks.
/// <para>
/// Expected JSON shape:
/// <code>
/// {
///   "nodes": [ { "id": "n1", "width": 320, "height": 150, "title": "…", … } ],
///   "ports": {
///     "n1": {
///       "top|bottom|left|right": [
///         { "id": "out:x", "links": [ { "target": "n2", "port": "in:y", "label": "…", "animated": false } ], … }
///       ]
///     }
///   }
/// }
/// </code>
/// </para>
/// </summary>
public sealed record InfiniteCanvasGraph(
    IReadOnlyList<JsonElement> Nodes,
    IReadOnlyDictionary<string, InfiniteCanvasNodePorts> NodePorts)
{
    /// <summary>Parses a graph from a JSON string. See the type doc for the expected shape.</summary>
    public static InfiniteCanvasGraph FromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return FromJson(doc.RootElement);
    }

    /// <summary>Parses a graph from a JSON object with "nodes" (array) and "ports" (map) members.</summary>
    public static InfiniteCanvasGraph FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("InfiniteCanvas graph JSON must be an object with 'nodes' and 'ports'.", nameof(root));
        }

        var nodes = new List<JsonElement>();
        if (root.TryGetProperty("nodes", out JsonElement nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement node in nodesEl.EnumerateArray())
            {
                nodes.Add(node.Clone());
            }
        }

        var ports = new Dictionary<string, InfiniteCanvasNodePorts>(StringComparer.Ordinal);
        if (root.TryGetProperty("ports", out JsonElement portsMap) && portsMap.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in portsMap.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Object && ParsePorts(entry.Value) is { } parsed)
                {
                    ports[entry.Name] = parsed;
                }
            }
        }

        return new InfiniteCanvasGraph(nodes, ports);
    }

    private static InfiniteCanvasNodePorts? ParsePorts(JsonElement portsEl)
    {
        IReadOnlyList<JsonElement>? top = ParseRail(portsEl, "top");
        IReadOnlyList<JsonElement>? bottom = ParseRail(portsEl, "bottom");
        IReadOnlyList<JsonElement>? left = ParseRail(portsEl, "left");
        IReadOnlyList<JsonElement>? right = ParseRail(portsEl, "right");
        return top is null && bottom is null && left is null && right is null
            ? null
            : new InfiniteCanvasNodePorts(top, bottom, left, right);
    }

    private static IReadOnlyList<JsonElement>? ParseRail(JsonElement portsEl, string side)
    {
        if (!portsEl.TryGetProperty(side, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<JsonElement>();
        foreach (JsonElement p in arr.EnumerateArray())
        {
            list.Add(p.Clone());
        }

        return list.Count == 0 ? null : list;
    }
}

public sealed class InfiniteCanvas : HookComponent<InfiniteCanvasProps>
{
    private const double MiniMapWidth = 220;
    private const double MiniMapHeight = 150;

    /// <summary>Runtime viewport: persisted pan/zoom plus the visible viewport size (for the minimap indicator).</summary>
    private readonly record struct ViewportState(
        double OffsetX,
        double OffsetY,
        double Zoom,
        double ViewportWidth,
        double ViewportHeight);

    public override Element Render()
    {
        InfiniteCanvasOptions options = Props.Options ?? new InfiniteCanvasOptions();

        var scrollViewerRef = UseRef<ScrollViewer?>(null);
        var canvasRef = UseRef<Canvas?>(null);

        // Drag state (imperative during a drag; committed to React state on release).
        var draggingKey = UseRef<string?>(null);
        var dragStartPointer = UseRef<Point>(new Point(0, 0));
        var dragStartPos = UseRef<InfiniteCanvasNodePosition>(new InfiniteCanvasNodePosition(0, 0));
        var dragLatest = UseRef<InfiniteCanvasNodePosition>(new InfiniteCanvasNodePosition(0, 0));

        // Mini-map viewport-indicator drag state. The main canvas only pans on release.
        var miniCanvasRef = UseRef<Canvas?>(null);
        var miniDragging = UseRef(false);
        var miniDragStartPointer = UseRef<Point>(new Point(0, 0));
        var miniDragStartRect = UseRef<Point>(new Point(0, 0));
        var miniDragLatest = UseRef<Point>(new Point(0, 0));

        // Mini-map node drag state (drag a node's mini rect; the real node moves on release).
        var miniNodeDragging = UseRef<string?>(null);
        var miniNodeDragStartPointer = UseRef<Point>(new Point(0, 0));
        var miniNodeDragStartRect = UseRef<Point>(new Point(0, 0));
        var miniNodeDragLatest = UseRef<Point>(new Point(0, 0));

        // Guards the one-time restore of a persisted viewport when the ScrollViewer first mounts.
        var restoredViewportRef = UseRef(false);

        // Canvas grab-to-pan state (drag empty canvas space to pan; nodes mark their own events
        // handled, so this only fires on empty space).
        var panning = UseRef(false);
        var panStartPointer = UseRef<Point>(new Point(0, 0));
        var panStartOffset = UseRef<Point>(new Point(0, 0));

        // Positions that have been explicitly moved this session (canvas-owned at runtime; live drags
        // win). Falls back to the persisted blob / GetInitialNodePos / auto-grid for anything not yet here.
        var (movedPositions, setMovedPositions) = UseState<IReadOnlyDictionary<string, InfiniteCanvasNodePosition>>(
            new Dictionary<string, InfiniteCanvasNodePosition>(StringComparer.Ordinal));

        // Reactive viewport (pan + zoom + visible size). Single source of truth that the ScrollViewer
        // commits into and the minimap indicator derives from — no live ScrollViewer reads, no tick hack.
        var (viewport, setViewport) = UseState<ViewportState?>(null);

        // Re-seed on StateKey change (frontend parity). When the host swaps StateKey it is declaring a
        // logically different graph: discard the live-drag layer and reset the viewport so the next
        // render re-seeds every node's geometry/viewport purely from CanvasState / GetInitialNodePos.
        // No-op on first mount (state already empty). Not one of the frozen seven.
        UseEffect(
            () =>
            {
                setMovedPositions(new Dictionary<string, InfiniteCanvasNodePosition>(StringComparer.Ordinal));
                setViewport(null);
                restoredViewportRef.Current = false;
                return () => { };
            },
            Props.StateKey ?? string.Empty);

        // Opaque persisted blob the canvas owns: node geometry + viewport live here. Read for seeding,
        // written back through OnCanvasStateCommit on every geometry/viewport change.
        JsonElement? blob = Props.CanvasState;

        // App theme, consumed from the nearest ThemeProvider via Reactor context (no static resource
        // lookups inside the canvas). Threaded into the builders below so grid/edges/nodes/minimap/zoom
        // all follow the active theme pack.
        AppTheme theme = UseContext(AppThemeContext.Current);

        // Resolve geometry for every current node and build its layout box. Position precedence:
        // runtime drag -> persisted blob -> GetInitialNodePos -> auto-grid. Size precedence: persisted
        // blob -> GetInitialNodePos -> default. `committed` doubles as the running `placed` map handed
        // to GetInitialNodePos (frontend parity); the live-drag draft layers on top of it.
        var boxes = new Dictionary<string, InfiniteCanvasNodeBox>(StringComparer.Ordinal);
        var committed = new Dictionary<string, InfiniteCanvasNodePosition>(StringComparer.Ordinal);
        for (int index = 0; index < Props.Nodes.Count; index++)
        {
            JsonElement descriptor = Props.Nodes[index];
            string id = NodeId(descriptor);
            (InfiniteCanvasNodePosition pos, double width, double height) =
                ResolveGeometry(descriptor, id, index, Props.Nodes, committed, movedPositions, blob, Props);
            committed[id] = pos;
            boxes[id] = new InfiniteCanvasNodeBox(id, width, height, descriptor);
        }

        // Live drag layer. During a drag the node pointer handlers write the in-flight position into the
        // draft; because that re-renders, the node shell, its port rails AND the connected edge ends all
        // move together (not just the node card moving imperatively). On release the position is lifted
        // into movedPositions (the base), and the matching draft entry prunes itself.
        DraftState<string, InfiniteCanvasNodePosition> positionDraft = UseDraftState(committed);

        // Resolve effective positions (base + live drag) into a concrete, mutable dictionary.
        var effective = new Dictionary<string, InfiniteCanvasNodePosition>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, InfiniteCanvasNodePosition> entry in positionDraft.Values)
        {
            effective[entry.Key] = entry.Value;
        }

        // Surface bounds (content extent + padding).
        double maxRight = 0;
        double maxBottom = 0;
        foreach ((string id, InfiniteCanvasNodeBox node) in boxes)
        {
            InfiniteCanvasNodePosition pos = effective[id];
            maxRight = Math.Max(maxRight, pos.X + node.Width);
            maxBottom = Math.Max(maxBottom, pos.Y + node.Height);
        }

        double surfaceWidth = Math.Max(1400, maxRight + options.ContentPadding);
        double surfaceHeight = Math.Max(900, maxBottom + options.ContentPadding);

        // Persist the opaque canvas blob (node geometry + viewport) through OnCanvasStateCommit. The
        // viewport override is passed because setViewport is async — the captured `viewport` is stale here.
        void Commit(ViewportState? viewportOverride = null)
        {
            if (Props.OnCanvasStateCommit is null)
            {
                return;
            }

            JsonElement next = BuildBlob(effective, boxes, viewportOverride ?? viewport, Props.CanvasState);
            Props.OnCanvasStateCommit(next);
        }

        // Commit viewport changes to state (re-renders the minimap) and persist via the blob.
        void CommitViewport(ViewportState next)
        {
            setViewport(next);
            Commit(next);
        }

        void CommitPosition(string id, InfiniteCanvasNodePosition committedPos)
        {
            var next = new Dictionary<string, InfiniteCanvasNodePosition>(movedPositions, StringComparer.Ordinal)
            {
                [id] = committedPos,
            };
            effective[id] = committedPos;
            setMovedPositions(next);
            Commit();
        }

        // ---- Canvas children: background grid + edges + nodes ----
        var children = new List<Element>();
        if (options.ShowGrid)
        {
            children.AddRange(BuildGridDots(surfaceWidth, surfaceHeight, options.GridSpacing, theme));
        }

        // Edges are derived internally from the declarative port links — the host declares connections
        // on the source ports, the canvas owns the whole edge layer. Rendered under the nodes so the
        // node cards sit on top of the connectors.
        IReadOnlyList<InfiniteCanvasEdge> edges = DeriveEdges(Props.NodePorts);
        if (edges.Count > 0)
        {
            children.AddRange(BuildEdges(edges, effective, boxes, Props.NodePorts, theme));
        }

        foreach ((string id, InfiniteCanvasNodeBox node) in boxes)
        {
            InfiniteCanvasNodePosition pos = effective[id];
            InfiniteCanvasNodePorts? nodePorts = null;
            Props.NodePorts?.TryGetValue(id, out nodePorts);
            children.Add(BuildNode(
                node,
                pos,
                nodePorts,
                canvasRef,
                draggingKey,
                dragStartPointer,
                dragStartPos,
                dragLatest,
                options,
                committedPos => CommitPosition(id, committedPos),
                movedPos => positionDraft.SetField(id, movedPos),
                theme));
        }

        Element surface =
            ScrollViewer(
                (Canvas(children.ToArray()) with
                {
                    Width = surfaceWidth,
                    Height = surfaceHeight,
                    Background = theme.Transparent,
                })
                .Set(canvas => canvasRef.Current = canvas)
                .OnPointerPressed((element, args) =>
                {
                    ScrollViewer? sv = scrollViewerRef.Current;
                    if (sv is null || element is not UIElement ui)
                    {
                        return;
                    }

                    Microsoft.UI.Input.PointerPoint pressed = args.GetCurrentPoint(sv);
                    bool primary = pressed.Properties.IsLeftButtonPressed
                        || args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse;
                    if (!primary)
                    {
                        return;
                    }

                    panning.Current = true;
                    panStartPointer.Current = pressed.Position;
                    panStartOffset.Current = new Point(sv.HorizontalOffset, sv.VerticalOffset);
                    ui.CapturePointer(args.Pointer);
                    args.Handled = true;
                })
                .OnPointerMoved((element, args) =>
                {
                    if (!panning.Current)
                    {
                        return;
                    }

                    ScrollViewer? sv = scrollViewerRef.Current;
                    if (sv is null)
                    {
                        return;
                    }

                    Point current = args.GetCurrentPoint(sv).Position;
                    double nextH = Math.Max(0, panStartOffset.Current.X - (current.X - panStartPointer.Current.X));
                    double nextV = Math.Max(0, panStartOffset.Current.Y - (current.Y - panStartPointer.Current.Y));
                    sv.ChangeView(nextH, nextV, null, disableAnimation: true);
                    args.Handled = true;
                })
                .OnPointerReleased((element, args) =>
                {
                    if (!panning.Current)
                    {
                        return;
                    }

                    panning.Current = false;
                    if (element is UIElement ui)
                    {
                        ui.ReleasePointerCapture(args.Pointer);
                    }

                    args.Handled = true;
                }))
            .ZoomMode(ZoomMode.Enabled)
            .HorizontalScrollMode(ScrollMode.Enabled)
            .VerticalScrollMode(ScrollMode.Enabled)
            .ViewChanged(args =>
            {
                if (args is not null && args.IsIntermediate)
                {
                    return;
                }

                ScrollViewer? sv = scrollViewerRef.Current;
                if (sv is null)
                {
                    return;
                }

                CommitViewport(new ViewportState(
                    sv.HorizontalOffset,
                    sv.VerticalOffset,
                    sv.ZoomFactor,
                    sv.ViewportWidth,
                    sv.ViewportHeight));
            })
            .Set(scrollViewer =>
            {
                scrollViewerRef.Current = scrollViewer;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.MinZoomFactor = (float)options.MinZoom;
                scrollViewer.MaxZoomFactor = (float)options.MaxZoom;
                AttachViewportFit(scrollViewer);
                RestoreSavedViewport(scrollViewer, BlobViewport(Props.CanvasState), restoredViewportRef);
            });

        var overlay = new List<Element> { surface };
        if (options.MiniMap != InfiniteCanvasMiniMapPlacement.Off)
        {
            overlay.Add(BuildMiniMap(
                boxes,
                effective,
                surfaceWidth,
                surfaceHeight,
                scrollViewerRef.Current,
                viewport,
                options,
                options.MiniMap,
                miniCanvasRef,
                miniDragging,
                miniDragStartPointer,
                miniDragStartRect,
                miniDragLatest,
                miniNodeDragging,
                miniNodeDragStartPointer,
                miniNodeDragStartRect,
                miniNodeDragLatest,
                CommitPosition,
                theme));
        }

        if (options.ShowZoomControls)
        {
            overlay.Add(BuildZoomControls(scrollViewerRef, effective, boxes, options, theme));
        }

        return Border(
                Grid(
                    new[] { GridSize.Star() },
                    new[] { GridSize.Star() },
                    overlay.ToArray()))
            .Background(theme.SurfaceBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(14)
            .Padding(12)
            .Flex(grow: 1);
    }

    // ---- Opaque-descriptor readers ----
    // The canvas treats node and port descriptors as opaque JSON; it reads only the structural fields
    // it needs and hands the whole descriptor to the host's render callbacks.

    private static string NodeId(JsonElement node) =>
        node.TryGetProperty("id", out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ArgumentException("InfiniteCanvas node descriptor is missing a string 'id'.");

    private static string PortId(JsonElement port) =>
        port.TryGetProperty("id", out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ArgumentException("InfiniteCanvas port descriptor is missing a string 'id'.");

    private static string? GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ---- Opaque CanvasState blob ----
    // Shape (private to the canvas; the consumer round-trips it verbatim):
    //   { "v": 1, "viewport": { "x", "y", "zoom" } | null, "nodes": { id: { "x", "y", "w", "h" } } }

    private static bool TryBlobNode(JsonElement? blob, string id, out JsonElement node)
    {
        node = default;
        return blob is { ValueKind: JsonValueKind.Object } b
            && b.TryGetProperty("nodes", out JsonElement nodes)
            && nodes.ValueKind == JsonValueKind.Object
            && nodes.TryGetProperty(id, out node)
            && node.ValueKind == JsonValueKind.Object;
    }

    private static InfiniteCanvasNodePosition? BlobNodePos(JsonElement? blob, string id) =>
        TryBlobNode(blob, id, out JsonElement n)
            && n.TryGetProperty("x", out JsonElement xe) && xe.ValueKind == JsonValueKind.Number
            && n.TryGetProperty("y", out JsonElement ye) && ye.ValueKind == JsonValueKind.Number
            ? new InfiniteCanvasNodePosition(xe.GetDouble(), ye.GetDouble())
            : null;

    private static (double Width, double Height)? BlobNodeSize(JsonElement? blob, string id) =>
        TryBlobNode(blob, id, out JsonElement n)
            && n.TryGetProperty("w", out JsonElement we) && we.ValueKind == JsonValueKind.Number
            && n.TryGetProperty("h", out JsonElement he) && he.ValueKind == JsonValueKind.Number
            ? (we.GetDouble(), he.GetDouble())
            : null;

    private static InfiniteCanvasViewport? BlobViewport(JsonElement? blob) =>
        blob is { ValueKind: JsonValueKind.Object } b
            && b.TryGetProperty("viewport", out JsonElement vp) && vp.ValueKind == JsonValueKind.Object
            && vp.TryGetProperty("x", out JsonElement xe) && xe.ValueKind == JsonValueKind.Number
            && vp.TryGetProperty("y", out JsonElement ye) && ye.ValueKind == JsonValueKind.Number
            && vp.TryGetProperty("zoom", out JsonElement ze) && ze.ValueKind == JsonValueKind.Number
            ? new InfiniteCanvasViewport(xe.GetDouble(), ye.GetDouble(), ze.GetDouble())
            : null;

    private static JsonElement BuildBlob(
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNodeBox> boxes,
        ViewportState? viewport,
        JsonElement? previousBlob)
    {
        var nodes = new JsonObject();
        foreach ((string id, InfiniteCanvasNodeBox box) in boxes)
        {
            InfiniteCanvasNodePosition pos = positions.TryGetValue(id, out InfiniteCanvasNodePosition? p)
                ? p
                : new InfiniteCanvasNodePosition(0, 0);
            nodes[id] = new JsonObject
            {
                ["x"] = JsonValue.Create(pos.X),
                ["y"] = JsonValue.Create(pos.Y),
                ["w"] = JsonValue.Create(box.Width),
                ["h"] = JsonValue.Create(box.Height),
            };
        }

        // Keep the previously-persisted viewport when this commit is geometry-only.
        InfiniteCanvasViewport? vp = viewport is { } v
            ? new InfiniteCanvasViewport(v.OffsetX, v.OffsetY, v.Zoom)
            : BlobViewport(previousBlob);
        JsonNode? viewportNode = vp is { } vv
            ? new JsonObject
            {
                ["x"] = JsonValue.Create(vv.OffsetX),
                ["y"] = JsonValue.Create(vv.OffsetY),
                ["zoom"] = JsonValue.Create(vv.Zoom),
            }
            : null;

        var blob = new JsonObject
        {
            ["v"] = JsonValue.Create(1),
            ["viewport"] = viewportNode,
            ["nodes"] = nodes,
        };

        using JsonDocument document = JsonDocument.Parse(blob.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Resolves a node's geometry. Position precedence: runtime drag -> persisted blob ->
    /// GetInitialNodePos -> auto-grid. Size precedence: persisted blob -> GetInitialNodePos -> default.
    /// GetInitialNodePos is invoked at most once per node (it returns both position and size).
    /// </summary>
    private static (InfiniteCanvasNodePosition Pos, double Width, double Height) ResolveGeometry(
        JsonElement descriptor,
        string id,
        int index,
        IReadOnlyList<JsonElement> nodes,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> placed,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> moved,
        JsonElement? blob,
        InfiniteCanvasProps props)
    {
        InfiniteCanvasNodeGeometry? seed = null;
        bool seedComputed = false;
        InfiniteCanvasNodeGeometry? Seed()
        {
            if (!seedComputed)
            {
                seedComputed = true;
                seed = props.GetInitialNodePos?.Invoke(
                    new InfiniteCanvasNodePlacement(descriptor, index, nodes.Count, nodes, placed));
            }

            return seed;
        }

        // Position.
        InfiniteCanvasNodePosition pos;
        if (moved.TryGetValue(id, out InfiniteCanvasNodePosition? movedPos))
        {
            pos = movedPos;
        }
        else if (BlobNodePos(blob, id) is { } blobPos)
        {
            pos = blobPos;
        }
        else if (Seed() is { } s && double.IsFinite(s.X) && double.IsFinite(s.Y))
        {
            pos = new InfiniteCanvasNodePosition(s.X, s.Y);
        }
        else
        {
            int column = index % 3;
            int row = index / 3;
            pos = new InfiniteCanvasNodePosition(80 + (column * 440), 80 + (row * 320));
        }

        // Size.
        double width;
        double height;
        if (BlobNodeSize(blob, id) is { } size)
        {
            (width, height) = size;
        }
        else if (Seed() is { } s2 && double.IsFinite(s2.Width) && double.IsFinite(s2.Height) && s2.Width > 0 && s2.Height > 0)
        {
            width = s2.Width;
            height = s2.Height;
        }
        else
        {
            width = 240;
            height = 140;
        }

        return (pos, width, height);
    }

    // ---- Node ----

    private Element BuildNode(
        InfiniteCanvasNodeBox box,
        InfiniteCanvasNodePosition pos,
        InfiniteCanvasNodePorts? ports,
        Microsoft.UI.Reactor.Core.Ref<Canvas?> canvasRef,
        Microsoft.UI.Reactor.Core.Ref<string?> draggingKey,
        Microsoft.UI.Reactor.Core.Ref<Point> dragStartPointer,
        Microsoft.UI.Reactor.Core.Ref<InfiniteCanvasNodePosition> dragStartPos,
        Microsoft.UI.Reactor.Core.Ref<InfiniteCanvasNodePosition> dragLatest,
        InfiniteCanvasOptions options,
        Action<InfiniteCanvasNodePosition> onCommitted,
        Action<InfiniteCanvasNodePosition> onDragMove,
        AppTheme theme)
    {
        string id = box.Id;

        // The node body is rendered entirely on demand by the host's RenderNode callback from the
        // opaque descriptor (no canvas-owned chrome — titles etc. are the consumer's to render).
        Element nodeContent = Props.RenderNode(box.Descriptor);

        // Body fills the node-local canvas (0,0 -> Width,Height). Ports straddle the borders.
        Element body =
            Border(Border(nodeContent).Padding(12))
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12)
            .Width(box.Width)
            .Height(box.Height)
            .Canvas(0, 0);

        var nodeChildren = new List<Element> { body };
        nodeChildren.AddRange(BuildNodePortChips(box, ports, Props.RenderNodePort, theme));

        return Canvas(nodeChildren.ToArray())
            .Width(box.Width)
            .Height(box.Height)
            .Canvas(pos.X, pos.Y)
            .OnPointerPressed((element, args) =>
            {
                Canvas? canvas = canvasRef.Current;
                if (canvas is null || element is not UIElement ui)
                {
                    return;
                }

                Point world = args.GetCurrentPoint(canvas).Position;
                draggingKey.Current = id;
                dragStartPointer.Current = world;
                dragStartPos.Current = pos;
                dragLatest.Current = pos;
                ui.CapturePointer(args.Pointer);
                args.Handled = true;
            })
            .OnPointerMoved((element, args) =>
            {
                if (!string.Equals(draggingKey.Current, id, StringComparison.Ordinal))
                {
                    return;
                }

                Canvas? canvas = canvasRef.Current;
                if (canvas is null)
                {
                    return;
                }

                Point world = args.GetCurrentPoint(canvas).Position;
                double nx = Math.Max(0, dragStartPos.Current.X + (world.X - dragStartPointer.Current.X));
                double ny = Math.Max(0, dragStartPos.Current.Y + (world.Y - dragStartPointer.Current.Y));
                var nextPos = new InfiniteCanvasNodePosition(nx, ny);
                dragLatest.Current = nextPos;

                // Drive the move through the reactive draft so the node shell, its port rails and the
                // connected edge ends all re-render together (no imperative-only node-card move).
                onDragMove(nextPos);
                args.Handled = true;
            })
            .OnPointerReleased((element, args) =>
            {
                if (!string.Equals(draggingKey.Current, id, StringComparison.Ordinal))
                {
                    return;
                }

                if (element is UIElement ui)
                {
                    ui.ReleasePointerCapture(args.Pointer);
                }

                draggingKey.Current = null;
                args.Handled = true;
                onCommitted(dragLatest.Current);
            })
            .WithKey($"icv-node-{id}");
    }

    // ---- Node port rails ----

    /// <summary>
    /// Lays out host-rendered port chips on the four node borders. Each chip is produced by the
    /// host's <paramref name="renderPort"/> callback and centred on its deterministic border anchor
    /// (matching <see cref="PortLocalAnchor"/>) via a SizeChanged recentre so the edge layer connects
    /// exactly to the visible chip. When no callback is supplied, no chips are rendered.
    /// </summary>
    private static IEnumerable<Element> BuildNodePortChips(
        InfiniteCanvasNodeBox box,
        InfiniteCanvasNodePorts? ports,
        Func<JsonElement, InfiniteCanvasPortRenderContext, Element>? renderPort,
        AppTheme theme)
    {
        if (ports is null || renderPort is null)
        {
            yield break;
        }

        foreach ((InfiniteCanvasPortSide side, IReadOnlyList<JsonElement>? list) in EnumeratePortSides(ports))
        {
            if (list is null || list.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                (double anchorX, double anchorY) = PortLocalAnchor(side, i, list.Count, box.Width, box.Height);
                Element content = renderPort(list[i], new InfiniteCanvasPortRenderContext(side, box.Descriptor));
                yield return BuildPortChip(content, anchorX, anchorY, theme);
            }
        }
    }

    private static Element BuildPortChip(Element content, double anchorX, double anchorY, AppTheme theme)
    {
        return Border(content)
            .Background(theme.Transparent)
            .Canvas(anchorX, anchorY)
            .Set(chip =>
            {
                void Recenter()
                {
                    Microsoft.UI.Xaml.Controls.Canvas.SetLeft(chip, anchorX - (chip.ActualWidth / 2));
                    Microsoft.UI.Xaml.Controls.Canvas.SetTop(chip, anchorY - (chip.ActualHeight / 2));
                }

                chip.SizeChanged += (_, _) => Recenter();
                Recenter();
            });
    }

    private static IEnumerable<(InfiniteCanvasPortSide Side, IReadOnlyList<JsonElement>? List)> EnumeratePortSides(InfiniteCanvasNodePorts ports)
    {
        yield return (InfiniteCanvasPortSide.Top, ports.Top);
        yield return (InfiniteCanvasPortSide.Bottom, ports.Bottom);
        yield return (InfiniteCanvasPortSide.Left, ports.Left);
        yield return (InfiniteCanvasPortSide.Right, ports.Right);
    }

    /// <summary>Node-local (top-left origin) anchor for port <paramref name="index"/> of <paramref name="count"/> on a side.</summary>
    private static (double X, double Y) PortLocalAnchor(InfiniteCanvasPortSide side, int index, int count, double width, double height)
    {
        double fraction = (index + 0.5) / Math.Max(1, count);
        return side switch
        {
            InfiniteCanvasPortSide.Top => (width * fraction, 0),
            InfiniteCanvasPortSide.Bottom => (width * fraction, height),
            InfiniteCanvasPortSide.Left => (0, height * fraction),
            InfiniteCanvasPortSide.Right => (width, height * fraction),
            _ => (width * fraction, height),
        };
    }

    // ---- Edge layer ----

    /// <summary>
    /// Builds the internal edge list from the declarative port links. Every port that declares a
    /// <see cref="InfiniteCanvasPortLink"/> contributes one connector, anchored from that port to the
    /// link's target port. This is what makes edges internal to the canvas rather than a host prop.
    /// </summary>
    private static IReadOnlyList<InfiniteCanvasEdge> DeriveEdges(
        IReadOnlyDictionary<string, InfiniteCanvasNodePorts>? nodePorts)
    {
        var edges = new List<InfiniteCanvasEdge>();
        if (nodePorts is null)
        {
            return edges;
        }

        foreach ((string nodeId, InfiniteCanvasNodePorts ports) in nodePorts)
        {
            foreach ((_, IReadOnlyList<JsonElement>? list) in EnumeratePortSides(ports))
            {
                if (list is null)
                {
                    continue;
                }

                foreach (JsonElement port in list)
                {
                    string portId = PortId(port);
                    if (!port.TryGetProperty("links", out JsonElement links) || links.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement link in links.EnumerateArray())
                    {
                        string? target = GetStr(link, "target") ?? GetStr(link, "targetNode");
                        string? targetPort = GetStr(link, "port") ?? GetStr(link, "targetPort");
                        if (target is null || targetPort is null)
                        {
                            continue;
                        }

                        bool animated = link.TryGetProperty("animated", out JsonElement a) && a.ValueKind == JsonValueKind.True;
                        edges.Add(new InfiniteCanvasEdge(
                            $"{nodeId}:{portId}->{target}:{targetPort}",
                            nodeId,
                            target,
                            portId,
                            targetPort,
                            GetStr(link, "label"),
                            animated));
                    }
                }
            }
        }

        return edges;
    }

    /// <summary>
    /// Draws bezier connectors between nodes in world space (so they pan/zoom with the surface).
    /// Edges anchor to a specific port when <c>SourcePort</c>/<c>TargetPort</c> resolve, otherwise to
    /// the source bottom-centre and target top-centre. Rendered beneath the node cards.
    /// </summary>
    private static IEnumerable<Element> BuildEdges(
        IReadOnlyList<InfiniteCanvasEdge> edges,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNodeBox> nodes,
        IReadOnlyDictionary<string, InfiniteCanvasNodePorts>? nodePorts,
        AppTheme theme)
    {
        IReadOnlyDictionary<(string Node, string Port), (double X, double Y, InfiniteCanvasPortSide Side)> anchors =
            BuildPortAnchors(positions, nodes, nodePorts);

        Brush stroke = theme.Accent;

        foreach (InfiniteCanvasEdge edge in edges)
        {
            if (!positions.TryGetValue(edge.Source, out InfiniteCanvasNodePosition? sourcePos)
                || !positions.TryGetValue(edge.Target, out InfiniteCanvasNodePosition? targetPos)
                || !nodes.TryGetValue(edge.Source, out InfiniteCanvasNodeBox? sourceNode)
                || !nodes.TryGetValue(edge.Target, out InfiniteCanvasNodeBox? targetNode))
            {
                continue;
            }

            (double sx, double sy, double sdx, double sdy) =
                ResolveEndpoint(anchors, edge.Source, edge.SourcePort, sourcePos, sourceNode, isSource: true);
            (double tx, double ty, double tdx, double tdy) =
                ResolveEndpoint(anchors, edge.Target, edge.TargetPort, targetPos, targetNode, isSource: false);

            double distance = Math.Sqrt(((tx - sx) * (tx - sx)) + ((ty - sy) * (ty - sy)));
            double off = Math.Clamp(distance * 0.4, 48, 160);

            // A Geometry can only be attached to a single Path, so build a fresh one per stroke.
            PathGeometry MakeGeometry() => new()
            {
                Figures =
                {
                    new PathFigure
                    {
                        StartPoint = new Point(sx, sy),
                        Segments =
                        {
                            new BezierSegment
                            {
                                Point1 = new Point(sx + (sdx * off), sy + (sdy * off)),
                                Point2 = new Point(tx + (tdx * off), ty + (tdy * off)),
                                Point3 = new Point(tx, ty),
                            },
                        },
                    },
                },
            };

            if (edge.Animated)
            {
                yield return Path2D()
                    .Set(path => path.Data = MakeGeometry())
                    .Stroke(stroke)
                    .StrokeThickness(2.4)
                    .StrokeDashArray(new[] { 2d, 3d })
                    .Opacity(0.85)
                    .WithKey($"icv-edge-flow-{edge.Id}");
            }

            yield return Path2D()
                .Set(path => path.Data = MakeGeometry())
                .Stroke(stroke)
                .StrokeThickness(edge.Animated ? 1.9 : 1.6)
                .Opacity(edge.Animated ? 0.95 : 0.78)
                .WithKey($"icv-edge-{edge.Id}");

            yield return Ellipse().Width(6).Height(6).Fill(stroke).Canvas(sx - 3, sy - 3).WithKey($"icv-edge-s-{edge.Id}");
            yield return Ellipse().Width(8).Height(8).Fill(stroke).Canvas(tx - 4, ty - 4).WithKey($"icv-edge-t-{edge.Id}");

            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                double cx = (sx + tx) / 2;
                double cy = (sy + ty) / 2;
                yield return Border(TextBlock(edge.Label).FontSize(11).Opacity(0.85))
                    .Padding(5, 1, 5, 1)
                    .Background(theme.Layer)
                    .WithBorder(theme.CardBorder, 1)
                    .CornerRadius(6)
                    .Canvas(Math.Max(0, cx - 24), Math.Max(0, cy - 10))
                    .WithKey($"icv-edge-label-{edge.Id}");
            }
        }
    }

    private static (double X, double Y, double Dx, double Dy) ResolveEndpoint(
        IReadOnlyDictionary<(string Node, string Port), (double X, double Y, InfiniteCanvasPortSide Side)> anchors,
        string nodeId,
        string? portId,
        InfiniteCanvasNodePosition pos,
        InfiniteCanvasNodeBox node,
        bool isSource)
    {
        if (portId is not null
            && anchors.TryGetValue((nodeId, portId), out (double X, double Y, InfiniteCanvasPortSide Side) anchor))
        {
            (double dx, double dy) = SideDirection(anchor.Side);
            return (anchor.X, anchor.Y, dx, dy);
        }

        // Fallback: source leaves the bottom-centre, target enters the top-centre.
        return isSource
            ? (pos.X + (node.Width / 2), pos.Y + node.Height, 0, 1)
            : (pos.X + (node.Width / 2), pos.Y, 0, -1);
    }

    private static (double Dx, double Dy) SideDirection(InfiniteCanvasPortSide side) => side switch
    {
        InfiniteCanvasPortSide.Top => (0, -1),
        InfiniteCanvasPortSide.Bottom => (0, 1),
        InfiniteCanvasPortSide.Left => (-1, 0),
        InfiniteCanvasPortSide.Right => (1, 0),
        _ => (0, 1),
    };

    private static IReadOnlyDictionary<(string Node, string Port), (double X, double Y, InfiniteCanvasPortSide Side)> BuildPortAnchors(
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNodeBox> nodes,
        IReadOnlyDictionary<string, InfiniteCanvasNodePorts>? nodePorts)
    {
        var anchors = new Dictionary<(string Node, string Port), (double X, double Y, InfiniteCanvasPortSide Side)>();
        if (nodePorts is null)
        {
            return anchors;
        }

        foreach ((string nodeId, InfiniteCanvasNodePorts ports) in nodePorts)
        {
            if (!positions.TryGetValue(nodeId, out InfiniteCanvasNodePosition? pos)
                || !nodes.TryGetValue(nodeId, out InfiniteCanvasNodeBox? node))
            {
                continue;
            }

            foreach ((InfiniteCanvasPortSide side, IReadOnlyList<JsonElement>? list) in EnumeratePortSides(ports))
            {
                if (list is null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    (double lx, double ly) = PortLocalAnchor(side, i, list.Count, node.Width, node.Height);
                    anchors[(nodeId, PortId(list[i]))] = (pos.X + lx, pos.Y + ly, side);
                }
            }
        }

        return anchors;
    }

    // ---- Background grid ----

    private static IEnumerable<Element> BuildGridDots(double width, double height, double spacing, AppTheme theme)
    {
        var brush = new SolidColorBrush(theme.GridDotColor);
        int columns = (int)(width / spacing);
        int rows = (int)(height / spacing);
        for (int row = 0; row <= rows; row++)
        {
            for (int column = 0; column <= columns; column++)
            {
                double x = column * spacing;
                double y = row * spacing;
                yield return Ellipse()
                    .Fill(brush)
                    .Width(2)
                    .Height(2)
                    .Canvas(x, y)
                    .WithKey($"icv-dot-{column}-{row}");
            }
        }
    }

    // ---- Mini-map ----

    private Element BuildMiniMap(
        IReadOnlyDictionary<string, InfiniteCanvasNodeBox> nodes,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        double surfaceWidth,
        double surfaceHeight,
        ScrollViewer? scrollViewer,
        ViewportState? viewport,
        InfiniteCanvasOptions options,
        InfiniteCanvasMiniMapPlacement placement,
        Microsoft.UI.Reactor.Core.Ref<Canvas?> miniCanvasRef,
        Microsoft.UI.Reactor.Core.Ref<bool> miniDragging,
        Microsoft.UI.Reactor.Core.Ref<Point> miniDragStartPointer,
        Microsoft.UI.Reactor.Core.Ref<Point> miniDragStartRect,
        Microsoft.UI.Reactor.Core.Ref<Point> miniDragLatest,
        Microsoft.UI.Reactor.Core.Ref<string?> miniNodeDragging,
        Microsoft.UI.Reactor.Core.Ref<Point> miniNodeDragStartPointer,
        Microsoft.UI.Reactor.Core.Ref<Point> miniNodeDragStartRect,
        Microsoft.UI.Reactor.Core.Ref<Point> miniNodeDragLatest,
        Action<string, InfiniteCanvasNodePosition> onCommitPosition,
        AppTheme theme)
    {
        // The minimap maps the full scrollable surface (0,0 -> surfaceWidth/Height), so it is a
        // faithful scaled picture of the canvas: the cards sit where they actually are and the
        // surrounding whitespace is proportional. The viewport is unioned in only when it extends
        // past the surface (e.g. zoomed out), so zooming out still grows the frame visibly.
        double worldLeft = 0;
        double worldTop = 0;
        double worldRight = surfaceWidth;
        double worldBottom = surfaceHeight;
        if (viewport is { Zoom: > 0 } worldVp)
        {
            double worldVx = worldVp.OffsetX / worldVp.Zoom;
            double worldVy = worldVp.OffsetY / worldVp.Zoom;
            worldLeft = Math.Min(worldLeft, worldVx);
            worldTop = Math.Min(worldTop, worldVy);
            worldRight = Math.Max(worldRight, worldVx + (worldVp.ViewportWidth / worldVp.Zoom));
            worldBottom = Math.Max(worldBottom, worldVy + (worldVp.ViewportHeight / worldVp.Zoom));
        }

        double worldWidth = Math.Max(1, worldRight - worldLeft);
        double worldHeight = Math.Max(1, worldBottom - worldTop);

        // Lock the minimap's shape to the viewport's aspect ratio (within a max box) so it stays a
        // constant size — it does not reshape as you pan/zoom, only when the window is resized. The
        // surface is then aspect-fit into that fixed box and centered (letterbox padding), so the
        // viewport frame fills the minimap exactly when the viewport matches the surface.
        double targetAspect = viewport is { ViewportWidth: > 0, ViewportHeight: > 0 } va
            ? va.ViewportWidth / va.ViewportHeight
            : surfaceWidth / surfaceHeight;
        double boxAspect = MiniMapWidth / MiniMapHeight;
        double miniWidth;
        double miniHeight;
        if (targetAspect >= boxAspect)
        {
            miniWidth = MiniMapWidth;
            miniHeight = MiniMapWidth / targetAspect;
        }
        else
        {
            miniHeight = MiniMapHeight;
            miniWidth = MiniMapHeight * targetAspect;
        }

        double scale = Math.Min(miniWidth / worldWidth, miniHeight / worldHeight);
        double padX = (miniWidth - (worldWidth * scale)) / 2;
        double padY = (miniHeight - (worldHeight * scale)) / 2;

        // World <-> minimap transforms (aspect-preserving, centered within the fixed-shape minimap).
        double ToMiniX(double worldX) => padX + ((worldX - worldLeft) * scale);
        double ToMiniY(double worldY) => padY + ((worldY - worldTop) * scale);
        double ToWorldX(double miniX) => worldLeft + ((miniX - padX) / scale);
        double ToWorldY(double miniY) => worldTop + ((miniY - padY) / scale);

        var miniChildren = new List<Element>();

        // Viewport indicator (draggable: pans the main canvas on release). Added before the node
        // rectangles so the nodes sit on top and stay grabbable even when the indicator is large.
        // Derives purely from reactive viewport state; the ScrollViewer is only the commit target.
        if (scrollViewer is not null && viewport is { Zoom: > 0, ViewportWidth: > 0 } vp)
        {
            double zoom = vp.Zoom;
            double vx = vp.OffsetX / zoom;
            double vy = vp.OffsetY / zoom;
            double vw = vp.ViewportWidth / zoom;
            double vh = vp.ViewportHeight / zoom;
            double rectW = Math.Max(8, vw * scale);
            double rectH = Math.Max(8, vh * scale);
            double rectLeft = ToMiniX(vx);
            double rectTop = ToMiniY(vy);
            ScrollViewer indicatorScrollViewer = scrollViewer;

            // When the viewport already covers the whole surface, the frame fills the minimap and
            // there is nowhere to pan to — so it is shown but not draggable (zoom in to make it
            // movable again).
            bool viewportCoversContent =
                vx <= worldLeft + 0.5 &&
                vy <= worldTop + 0.5 &&
                (vx + vw) >= worldRight - 0.5 &&
                (vy + vh) >= worldBottom - 0.5;

            var indicator = Rectangle()
                .Fill(new SolidColorBrush(theme.MiniMapViewportFill))
                .WithBorder(theme.TextPrimary, 1.5)
                .Width(rectW)
                .Height(rectH)
                .Canvas(rectLeft, rectTop)
                .WithKey("icv-mini-viewport");

            if (!viewportCoversContent)
            {
                indicator = indicator
                    .OnPointerPressed((element, args) =>
                    {
                        Canvas? mini = miniCanvasRef.Current;
                        if (mini is null || element is not UIElement ui)
                        {
                            return;
                        }

                        Point pointer = args.GetCurrentPoint(mini).Position;
                        miniDragging.Current = true;
                        miniDragStartPointer.Current = pointer;
                        miniDragStartRect.Current = new Point(rectLeft, rectTop);
                        miniDragLatest.Current = new Point(rectLeft, rectTop);
                        ui.CapturePointer(args.Pointer);
                        args.Handled = true;
                    })
                    .OnPointerMoved((element, args) =>
                    {
                        if (!miniDragging.Current)
                        {
                            return;
                        }

                        Canvas? mini = miniCanvasRef.Current;
                        if (mini is null || element is not UIElement ui)
                        {
                            return;
                        }

                        Point pointer = args.GetCurrentPoint(mini).Position;
                        double nx = Math.Clamp(miniDragStartRect.Current.X + (pointer.X - miniDragStartPointer.Current.X), 0, Math.Max(0, miniWidth - rectW));
                        double ny = Math.Clamp(miniDragStartRect.Current.Y + (pointer.Y - miniDragStartPointer.Current.Y), 0, Math.Max(0, miniHeight - rectH));
                        miniDragLatest.Current = new Point(nx, ny);

                        // Move the indicator imperatively; the main canvas only follows on release.
                        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(ui, nx);
                        Microsoft.UI.Xaml.Controls.Canvas.SetTop(ui, ny);
                        args.Handled = true;
                    })
                    .OnPointerReleased((element, args) =>
                    {
                        if (!miniDragging.Current)
                        {
                            return;
                        }

                        miniDragging.Current = false;
                        if (element is UIElement ui)
                        {
                            ui.ReleasePointerCapture(args.Pointer);
                        }

                        args.Handled = true;

                        if (scale <= 0)
                        {
                            return;
                        }

                        double worldX = ToWorldX(miniDragLatest.Current.X);
                        double worldY = ToWorldY(miniDragLatest.Current.Y);
                        indicatorScrollViewer.ChangeView(Math.Max(0, worldX * zoom), Math.Max(0, worldY * zoom), (float)zoom);
                    });
            }

            miniChildren.Add(indicator);
        }

        // Node rectangles (draggable: the real node on the canvas moves on release).
        foreach ((string id, InfiniteCanvasNodeBox node) in nodes)
        {
            InfiniteCanvasNodePosition pos = positions[id];
            double nodeRectW = Math.Max(6, node.Width * scale);
            double nodeRectH = Math.Max(6, node.Height * scale);
            double nodeLeft = ToMiniX(pos.X);
            double nodeTop = ToMiniY(pos.Y);
            string nodeId = id;
            miniChildren.Add(Rectangle()
                .Fill(theme.Accent)
                .Width(nodeRectW)
                .Height(nodeRectH)
                .Canvas(nodeLeft, nodeTop)
                .OnPointerPressed((element, args) =>
                {
                    Canvas? mini = miniCanvasRef.Current;
                    if (mini is null || element is not UIElement ui)
                    {
                        return;
                    }

                    Point pointer = args.GetCurrentPoint(mini).Position;
                    miniNodeDragging.Current = nodeId;
                    miniNodeDragStartPointer.Current = pointer;
                    miniNodeDragStartRect.Current = new Point(nodeLeft, nodeTop);
                    miniNodeDragLatest.Current = new Point(nodeLeft, nodeTop);
                    ui.CapturePointer(args.Pointer);
                    args.Handled = true;
                })
                .OnPointerMoved((element, args) =>
                {
                    if (!string.Equals(miniNodeDragging.Current, nodeId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    Canvas? mini = miniCanvasRef.Current;
                    if (mini is null || element is not UIElement ui)
                    {
                        return;
                    }

                    Point pointer = args.GetCurrentPoint(mini).Position;
                    double nx = Math.Clamp(miniNodeDragStartRect.Current.X + (pointer.X - miniNodeDragStartPointer.Current.X), 0, Math.Max(0, miniWidth - nodeRectW));
                    double ny = Math.Clamp(miniNodeDragStartRect.Current.Y + (pointer.Y - miniNodeDragStartPointer.Current.Y), 0, Math.Max(0, miniHeight - nodeRectH));
                    miniNodeDragLatest.Current = new Point(nx, ny);

                    // Move the mini rect imperatively; the real node only follows on release.
                    Microsoft.UI.Xaml.Controls.Canvas.SetLeft(ui, nx);
                    Microsoft.UI.Xaml.Controls.Canvas.SetTop(ui, ny);
                    args.Handled = true;
                })
                .OnPointerReleased((element, args) =>
                {
                    if (!string.Equals(miniNodeDragging.Current, nodeId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    miniNodeDragging.Current = null;
                    if (element is UIElement ui)
                    {
                        ui.ReleasePointerCapture(args.Pointer);
                    }

                    args.Handled = true;

                    if (scale <= 0)
                    {
                        return;
                    }

                    double worldX = ToWorldX(miniNodeDragLatest.Current.X);
                    double worldY = ToWorldY(miniNodeDragLatest.Current.Y);
                    onCommitPosition(nodeId, new InfiniteCanvasNodePosition(worldX, worldY));
                })
                .WithKey($"icv-mini-{nodeId}"));
        }

        HorizontalAlignment horizontal = placement is InfiniteCanvasMiniMapPlacement.BottomLeft or InfiniteCanvasMiniMapPlacement.TopLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        VerticalAlignment vertical = placement is InfiniteCanvasMiniMapPlacement.TopLeft or InfiniteCanvasMiniMapPlacement.TopRight
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;

        return Border(
                (Canvas(miniChildren.ToArray()) with
                {
                    Width = miniWidth,
                    Height = miniHeight,
                    Background = theme.Transparent,
                })
                .Set(canvas => miniCanvasRef.Current = canvas)
                .OnPointerWheelChanged((element, args) =>
                {
                    // Match the main canvas (a ScrollViewer): plain wheel pans, Shift+wheel pans
                    // horizontally, Ctrl+wheel zooms toward the region under the cursor.
                    Canvas? mini = miniCanvasRef.Current;
                    if (scrollViewer is null || mini is null || scale <= 0)
                    {
                        return;
                    }

                    Microsoft.UI.Input.PointerPoint point = args.GetCurrentPoint(mini);
                    int delta = point.Properties.MouseWheelDelta;

                    if ((args.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != 0)
                    {
                        double factor = delta > 0 ? 1.15 : 1 / 1.15;
                        float currentZoom = scrollViewer.ZoomFactor > 0 ? scrollViewer.ZoomFactor : 1f;
                        float targetZoom = (float)Math.Clamp(currentZoom * factor, options.MinZoom, options.MaxZoom);
                        double worldX = ToWorldX(point.Position.X);
                        double worldY = ToWorldY(point.Position.Y);
                        double zoomOffsetX = Math.Max(0, (worldX * targetZoom) - (scrollViewer.ViewportWidth / 2));
                        double zoomOffsetY = Math.Max(0, (worldY * targetZoom) - (scrollViewer.ViewportHeight / 2));
                        scrollViewer.ChangeView(zoomOffsetX, zoomOffsetY, targetZoom);
                    }
                    else if ((args.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0)
                    {
                        scrollViewer.ChangeView(Math.Max(0, scrollViewer.HorizontalOffset - delta), null, null);
                    }
                    else
                    {
                        scrollViewer.ChangeView(null, Math.Max(0, scrollViewer.VerticalOffset - delta), null);
                    }

                    args.Handled = true;
                }))
            .Background(theme.LayerAlt)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(8)
            .Padding(4)
            .HAlign(horizontal)
            .VAlign(vertical);
    }

    // ---- Zoom controls ----

    private Element BuildZoomControls(
        Microsoft.UI.Reactor.Core.Ref<ScrollViewer?> scrollViewerRef,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNodeBox> nodes,
        InfiniteCanvasOptions options,
        AppTheme theme)
    {
        // Floating icon buttons stacked as a single connected toolbar group: a bordered container
        // holds borderless square buttons with zero spacing, anchored bottom-left with a small margin.
        return Border(
                (VStack(0,
                    ZoomIconButton(theme, HostIconSources.ControlZoomIn, "Zoom in", () => ZoomBy(scrollViewerRef.Current, 1.2, options)),
                    ZoomIconButton(theme, HostIconSources.ControlZoomOut, "Zoom out", () => ZoomBy(scrollViewerRef.Current, 1 / 1.2, options)),
                    ZoomIconButton(theme, HostIconSources.ControlFitView, "Fit to content", () => FitToContent(scrollViewerRef.Current, positions, nodes, options)),
                    ZoomIconButton(theme, HostIconSources.ControlActualSize, "Actual size", () => ResetZoom(scrollViewerRef.Current)))
                .Set(panel => panel.Spacing = 0)))
            .Background(theme.Layer)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(8)
            .Padding(0)
            .HAlign(HorizontalAlignment.Left)
            .VAlign(VerticalAlignment.Bottom)
            .Margin(16, 0, 0, 16);
    }

    private static Element ZoomIconButton(AppTheme theme, string iconSource, string automationName, Action onClick)
    {
        // Borderless, transparent square button so the buttons merge into the container group with no
        // visible gaps; hover/press still highlights the individual button.
        return Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(iconSource)), onClick)
            .Background(theme.SurfaceElevated)
            .AutomationName(automationName)
            .Foreground(theme.AccentStrong)
            .Opacity(0.75)
            .Width(40)
            .Height(40)
            .CornerRadius(0)
            .Margin(0, 0, 0, 0)
            .Set(button =>
            {
                button.BorderThickness = new Thickness(0);
                button.BorderBrush = null;
                button.Padding = new Thickness(0);
            });
    }

    private static void ZoomBy(ScrollViewer? scrollViewer, double factor, InfiniteCanvasOptions options)
    {
        if (scrollViewer is null)
        {
            return;
        }

        float current = scrollViewer.ZoomFactor > 0 ? scrollViewer.ZoomFactor : 1;
        float target = (float)Math.Clamp(current * factor, options.MinZoom, options.MaxZoom);
        double centerX = (scrollViewer.HorizontalOffset + (scrollViewer.ViewportWidth / 2)) / current;
        double centerY = (scrollViewer.VerticalOffset + (scrollViewer.ViewportHeight / 2)) / current;
        double offsetX = Math.Max(0, (centerX * target) - (scrollViewer.ViewportWidth / 2));
        double offsetY = Math.Max(0, (centerY * target) - (scrollViewer.ViewportHeight / 2));
        scrollViewer.ChangeView(offsetX, offsetY, target);
    }

    private static void ResetZoom(ScrollViewer? scrollViewer)
    {
        scrollViewer?.ChangeView(scrollViewer.HorizontalOffset, scrollViewer.VerticalOffset, 1f);
    }

    private static void FitToContent(
        ScrollViewer? scrollViewer,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNodeBox> nodes,
        InfiniteCanvasOptions options)
    {
        if (scrollViewer is null || nodes.Count == 0)
        {
            return;
        }

        double minLeft = double.MaxValue;
        double minTop = double.MaxValue;
        double maxRight = double.MinValue;
        double maxBottom = double.MinValue;
        foreach ((string id, InfiniteCanvasNodeBox node) in nodes)
        {
            InfiniteCanvasNodePosition pos = positions[id];
            minLeft = Math.Min(minLeft, pos.X);
            minTop = Math.Min(minTop, pos.Y);
            maxRight = Math.Max(maxRight, pos.X + node.Width);
            maxBottom = Math.Max(maxBottom, pos.Y + node.Height);
        }

        double boundsWidth = Math.Max(1, maxRight - minLeft);
        double boundsHeight = Math.Max(1, maxBottom - minTop);
        double viewportWidth = scrollViewer.ViewportWidth > 0 ? scrollViewer.ViewportWidth : 1200;
        double viewportHeight = scrollViewer.ViewportHeight > 0 ? scrollViewer.ViewportHeight : 800;
        double zoom = Math.Min((viewportWidth * 0.85) / boundsWidth, (viewportHeight * 0.85) / boundsHeight);
        zoom = Math.Clamp(zoom, options.MinZoom, options.MaxZoom);

        double centerX = minLeft + (boundsWidth / 2);
        double centerY = minTop + (boundsHeight / 2);
        double offsetX = Math.Max(0, (centerX * zoom) - (viewportWidth / 2));
        double offsetY = Math.Max(0, (centerY * zoom) - (viewportHeight / 2));
        scrollViewer.ChangeView(offsetX, offsetY, (float)zoom);
    }

    // ---- Viewport fitting ----
    // The app's vertical StackPanels never grant a bounded height, so a ScrollViewer would
    // otherwise size to its content and never scroll. We fit the viewport to the remaining
    // window space so the canvas behaves as a true fixed viewport.

    private static void AttachViewportFit(ScrollViewer scrollViewer)
    {
        void Fit()
        {
            XamlRoot? root = scrollViewer.XamlRoot;
            if (root?.Content is null)
            {
                return;
            }

            try
            {
                GeneralTransform transform = scrollViewer.TransformToVisual(root.Content);
                Point origin = transform.TransformPoint(new Point(0, 0));
                double availableWidth = root.Size.Width - origin.X - 24;
                double availableHeight = root.Size.Height - origin.Y - 24;
                if (availableWidth > 160)
                {
                    scrollViewer.Width = availableWidth;
                }

                if (availableHeight > 160)
                {
                    scrollViewer.Height = availableHeight;
                }
            }
            catch
            {
            }
        }

        scrollViewer.Loaded += (_, _) => Fit();
        if (scrollViewer.XamlRoot is not null)
        {
            scrollViewer.XamlRoot.Changed += (_, _) => Fit();
        }
    }

    // ---- Viewport restore ----
    // Re-applies a persisted viewport (pan + zoom) once, when the ScrollViewer first mounts, so a saved
    // layout reopens exactly where the user left it — symmetric with how node positions restore.

    private static void RestoreSavedViewport(
        ScrollViewer scrollViewer,
        InfiniteCanvasViewport? saved,
        Microsoft.UI.Reactor.Core.Ref<bool> handled)
    {
        if (saved is null || handled.Current)
        {
            return;
        }

        handled.Current = true;

        void Apply()
        {
            scrollViewer.ChangeView(saved.OffsetX, saved.OffsetY, (float)saved.Zoom, disableAnimation: true);
        }

        if (scrollViewer.IsLoaded)
        {
            Apply();
        }
        else
        {
            scrollViewer.Loaded += (_, _) => Apply();
        }
    }
}
