using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Hooks;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

// =====================================================================================
//  InfiniteCanvas — an independent, declarative pan/zoom surface for arbitrary Reactor
//  components. It is a *controlled* component:
//
//    • Nodes            : map of id -> node (content + intrinsic size). WHAT to render.
//    • SavedLayout      : persisted positions (+ viewport). WHERE things are.
//    • GetInitialPosition: fallback seed for ids not present in SavedLayout.
//    • OnLayoutChange   : fired when positions/viewport change (drag / pan / zoom);
//                         the host persists it and feeds it back as SavedLayout.
//
//  The canvas owns viewport + presentation chrome only (grid, minimap, zoom controls).
//  It knows nothing about boards or cards.
// =====================================================================================

/// <summary>A single placeable node: any Reactor element plus its intrinsic size.</summary>
public sealed record InfiniteCanvasNode(
    Element Content,
    double Width,
    double Height,
    string? Title = null);

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
/// A single connection point on a node border. <see cref="Content"/> is host-rendered
/// (e.g. a handle chip / button); the canvas only lays it out on the rail and exposes its
/// anchor point to the edge layer via <paramref name="Id"/> (unique within its node).
/// </summary>
public sealed record InfiniteCanvasPort(string Id, Element Content);

/// <summary>Per-side port rails for one node (top / bottom / left / right).</summary>
public sealed record InfiniteCanvasNodePorts(
    IReadOnlyList<InfiniteCanvasPort>? Top = null,
    IReadOnlyList<InfiniteCanvasPort>? Bottom = null,
    IReadOnlyList<InfiniteCanvasPort>? Left = null,
    IReadOnlyList<InfiniteCanvasPort>? Right = null);

/// <summary>
/// A connection drawn between two nodes. When <see cref="SourcePort"/> / <see cref="TargetPort"/>
/// are supplied they anchor the curve to that specific port; otherwise it falls back to the
/// source's bottom-centre and the target's top-centre.
/// </summary>
public sealed record InfiniteCanvasEdge(
    string Id,
    string Source,
    string Target,
    string? SourcePort = null,
    string? TargetPort = null,
    string? Label = null,
    bool Animated = false);

/// <summary>Pan (content offset in view px) + zoom factor.</summary>
public sealed record InfiniteCanvasViewport(double OffsetX, double OffsetY, double Zoom);

/// <summary>The full persisted layout that round-trips through the host.</summary>
public sealed record InfiniteCanvasLayout(
    IReadOnlyDictionary<string, InfiniteCanvasNodePosition> Positions,
    InfiniteCanvasViewport? Viewport = null);

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

public sealed record InfiniteCanvasProps(
    IReadOnlyDictionary<string, InfiniteCanvasNode> Nodes,
    InfiniteCanvasLayout? SavedLayout = null,
    Func<string, InfiniteCanvasNodePosition>? GetInitialPosition = null,
    Action<InfiniteCanvasLayout>? OnLayoutChange = null,
    InfiniteCanvasOptions? Options = null,
    IReadOnlyDictionary<string, InfiniteCanvasNodePorts>? NodePorts = null,
    IReadOnlyList<InfiniteCanvasEdge>? Edges = null);

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

        // Positions that have been explicitly moved this session. Falls back to SavedLayout /
        // GetInitialPosition / auto-grid for anything not yet here.
        var (movedPositions, setMovedPositions) = UseState<IReadOnlyDictionary<string, InfiniteCanvasNodePosition>>(
            new Dictionary<string, InfiniteCanvasNodePosition>(StringComparer.Ordinal));

        // Reactive viewport (pan + zoom + visible size). Single source of truth that the ScrollViewer
        // commits into and the minimap indicator derives from — no live ScrollViewer reads, no tick hack.
        var (viewport, setViewport) = UseState<ViewportState?>(null);

        // Committed positions for every current node: movedPositions -> SavedLayout ->
        // GetInitialPosition -> auto-grid. This is the *base* the live-drag draft layers on top of.
        var committed = new Dictionary<string, InfiniteCanvasNodePosition>(StringComparer.Ordinal);
        int autoIndex = 0;
        foreach (string id in Props.Nodes.Keys)
        {
            committed[id] = ResolvePosition(id, autoIndex, movedPositions, Props);
            autoIndex++;
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
        foreach ((string id, InfiniteCanvasNode node) in Props.Nodes)
        {
            InfiniteCanvasNodePosition pos = effective[id];
            maxRight = Math.Max(maxRight, pos.X + node.Width);
            maxBottom = Math.Max(maxBottom, pos.Y + node.Height);
        }

        double surfaceWidth = Math.Max(1400, maxRight + options.ContentPadding);
        double surfaceHeight = Math.Max(900, maxBottom + options.ContentPadding);

        void EmitLayout(ViewportState? viewportOverride = null)
        {
            if (Props.OnLayoutChange is null)
            {
                return;
            }

            ViewportState? current = viewportOverride ?? viewport;
            InfiniteCanvasViewport? persisted = current is { } v
                ? new InfiniteCanvasViewport(v.OffsetX, v.OffsetY, v.Zoom)
                : Props.SavedLayout?.Viewport;

            Props.OnLayoutChange(new InfiniteCanvasLayout(
                new Dictionary<string, InfiniteCanvasNodePosition>(effective, StringComparer.Ordinal),
                persisted));
        }

        // Commit viewport changes to state (re-renders the minimap) and persist via OnLayoutChange.
        // The override is passed because setViewport is async — the captured `viewport` is still stale here.
        void CommitViewport(ViewportState next)
        {
            setViewport(next);
            EmitLayout(next);
        }

        void CommitPosition(string id, InfiniteCanvasNodePosition committedPos)
        {
            var next = new Dictionary<string, InfiniteCanvasNodePosition>(movedPositions, StringComparer.Ordinal)
            {
                [id] = committedPos,
            };
            effective[id] = committedPos;
            setMovedPositions(next);
            EmitLayout();
        }

        // ---- Canvas children: background grid + edges + nodes ----
        var children = new List<Element>();
        if (options.ShowGrid)
        {
            children.AddRange(BuildGridDots(surfaceWidth, surfaceHeight, options.GridSpacing));
        }

        // Edges render under the nodes so the node cards sit on top of the connectors.
        if (Props.Edges is { Count: > 0 } edges)
        {
            children.AddRange(BuildEdges(edges, effective, Props.Nodes, Props.NodePorts));
        }

        foreach ((string id, InfiniteCanvasNode node) in Props.Nodes)
        {
            InfiniteCanvasNodePosition pos = effective[id];
            InfiniteCanvasNodePorts? nodePorts = null;
            Props.NodePorts?.TryGetValue(id, out nodePorts);
            children.Add(BuildNode(
                id,
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
                movedPos => positionDraft.SetField(id, movedPos)));
        }

        Element surface =
            ScrollViewer(
                (Canvas(children.ToArray()) with
                {
                    Width = surfaceWidth,
                    Height = surfaceHeight,
                    Background = new SolidColorBrush(Colors.Transparent),
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
                RestoreSavedViewport(scrollViewer, Props.SavedLayout?.Viewport, restoredViewportRef);
            });

        var overlay = new List<Element> { surface };
        if (options.MiniMap != InfiniteCanvasMiniMapPlacement.Off)
        {
            overlay.Add(BuildMiniMap(
                Props.Nodes,
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
                CommitPosition));
        }

        if (options.ShowZoomControls)
        {
            overlay.Add(BuildZoomControls(scrollViewerRef, effective, Props.Nodes, options));
        }

        return Border(
                Grid(
                    new[] { GridSize.Star() },
                    new[] { GridSize.Star() },
                    overlay.ToArray()))
            .Background(ReactorMainShellComponent.ResolveBrush("SolidBackgroundFillColorBaseAltBrush"))
            .WithBorder(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"), 1)
            .CornerRadius(14)
            .Padding(12)
            .Flex(grow: 1);
    }

    private static InfiniteCanvasNodePosition ResolvePosition(
        string id,
        int index,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> moved,
        InfiniteCanvasProps props)
    {
        if (moved.TryGetValue(id, out InfiniteCanvasNodePosition? movedPos))
        {
            return movedPos;
        }

        if (props.SavedLayout?.Positions.TryGetValue(id, out InfiniteCanvasNodePosition? savedPos) == true)
        {
            return savedPos;
        }

        if (props.GetInitialPosition is not null)
        {
            return props.GetInitialPosition(id);
        }

        int column = index % 3;
        int row = index / 3;
        return new InfiniteCanvasNodePosition(80 + (column * 440), 80 + (row * 320));
    }

    // ---- Node ----

    private Element BuildNode(
        string id,
        InfiniteCanvasNode node,
        InfiniteCanvasNodePosition pos,
        InfiniteCanvasNodePorts? ports,
        Microsoft.UI.Reactor.Core.Ref<Canvas?> canvasRef,
        Microsoft.UI.Reactor.Core.Ref<string?> draggingKey,
        Microsoft.UI.Reactor.Core.Ref<Point> dragStartPointer,
        Microsoft.UI.Reactor.Core.Ref<InfiniteCanvasNodePosition> dragStartPos,
        Microsoft.UI.Reactor.Core.Ref<InfiniteCanvasNodePosition> dragLatest,
        InfiniteCanvasOptions options,
        Action<InfiniteCanvasNodePosition> onCommitted,
        Action<InfiniteCanvasNodePosition> onDragMove)
    {
        Element header = node.Title is null
            ? Rectangle().Fill(new SolidColorBrush(Colors.Transparent)).Height(0)
            : Border(TextBlock(node.Title).Bold().FontSize(13))
                .Padding(12, 8, 12, 8)
                .Background(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"));

        // Body fills the node-local canvas (0,0 -> Width,Height). Ports straddle the borders.
        Element body =
            Border(
                VStack(0,
                    header,
                    Border(node.Content).Padding(12)))
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorDefaultBrush"))
            .WithBorder(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"), 1)
            .CornerRadius(12)
            .Width(node.Width)
            .Height(node.Height)
            .Canvas(0, 0);

        var nodeChildren = new List<Element> { body };
        nodeChildren.AddRange(BuildNodePortChips(node, ports));

        return Canvas(nodeChildren.ToArray())
            .Width(node.Width)
            .Height(node.Height)
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
    /// Lays out host-rendered port chips on the four node borders. Each chip is centred on its
    /// deterministic border anchor (matching <see cref="PortLocalAnchor"/>) via a SizeChanged
    /// recentre so the edge layer connects exactly to the visible chip.
    /// </summary>
    private static IEnumerable<Element> BuildNodePortChips(InfiniteCanvasNode node, InfiniteCanvasNodePorts? ports)
    {
        if (ports is null)
        {
            yield break;
        }

        foreach ((InfiniteCanvasPortSide side, IReadOnlyList<InfiniteCanvasPort>? list) in EnumeratePortSides(ports))
        {
            if (list is null || list.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                (double anchorX, double anchorY) = PortLocalAnchor(side, i, list.Count, node.Width, node.Height);
                yield return BuildPortChip(list[i].Content, anchorX, anchorY);
            }
        }
    }

    private static Element BuildPortChip(Element content, double anchorX, double anchorY)
    {
        return Border(content)
            .Background(new SolidColorBrush(Colors.Transparent))
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

    private static IEnumerable<(InfiniteCanvasPortSide Side, IReadOnlyList<InfiniteCanvasPort>? List)> EnumeratePortSides(InfiniteCanvasNodePorts ports)
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
    /// Draws bezier connectors between nodes in world space (so they pan/zoom with the surface).
    /// Edges anchor to a specific port when <c>SourcePort</c>/<c>TargetPort</c> resolve, otherwise to
    /// the source bottom-centre and target top-centre. Rendered beneath the node cards.
    /// </summary>
    private static IEnumerable<Element> BuildEdges(
        IReadOnlyList<InfiniteCanvasEdge> edges,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNode> nodes,
        IReadOnlyDictionary<string, InfiniteCanvasNodePorts>? nodePorts)
    {
        IReadOnlyDictionary<(string Node, string Port), (double X, double Y, InfiniteCanvasPortSide Side)> anchors =
            BuildPortAnchors(positions, nodes, nodePorts);

        Brush stroke = ReactorMainShellComponent.ResolveBrush("AccentFillColorDefaultBrush");

        foreach (InfiniteCanvasEdge edge in edges)
        {
            if (!positions.TryGetValue(edge.Source, out InfiniteCanvasNodePosition? sourcePos)
                || !positions.TryGetValue(edge.Target, out InfiniteCanvasNodePosition? targetPos)
                || !nodes.TryGetValue(edge.Source, out InfiniteCanvasNode? sourceNode)
                || !nodes.TryGetValue(edge.Target, out InfiniteCanvasNode? targetNode))
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
                    .Background(ReactorMainShellComponent.ResolveBrush("LayerFillColorDefaultBrush"))
                    .WithBorder(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"), 1)
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
        InfiniteCanvasNode node,
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
        IReadOnlyDictionary<string, InfiniteCanvasNode> nodes,
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
                || !nodes.TryGetValue(nodeId, out InfiniteCanvasNode? node))
            {
                continue;
            }

            foreach ((InfiniteCanvasPortSide side, IReadOnlyList<InfiniteCanvasPort>? list) in EnumeratePortSides(ports))
            {
                if (list is null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    (double lx, double ly) = PortLocalAnchor(side, i, list.Count, node.Width, node.Height);
                    anchors[(nodeId, list[i].Id)] = (pos.X + lx, pos.Y + ly, side);
                }
            }
        }

        return anchors;
    }

    // ---- Background grid ----

    private static IEnumerable<Element> BuildGridDots(double width, double height, double spacing)
    {
        var brush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0x88, 0x88, 0x88));
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
        IReadOnlyDictionary<string, InfiniteCanvasNode> nodes,
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
        Action<string, InfiniteCanvasNodePosition> onCommitPosition)
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
                .Fill(new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x30, 0x90, 0xF0)))
                .WithBorder(ReactorMainShellComponent.ResolveBrush("TextFillColorPrimaryBrush"), 1.5)
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
        foreach ((string id, InfiniteCanvasNode node) in nodes)
        {
            InfiniteCanvasNodePosition pos = positions[id];
            double nodeRectW = Math.Max(6, node.Width * scale);
            double nodeRectH = Math.Max(6, node.Height * scale);
            double nodeLeft = ToMiniX(pos.X);
            double nodeTop = ToMiniY(pos.Y);
            string nodeId = id;
            miniChildren.Add(Rectangle()
                .Fill(ReactorMainShellComponent.ResolveBrush("AccentFillColorDefaultBrush"))
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
                    Background = new SolidColorBrush(Colors.Transparent),
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
            .Background(ReactorMainShellComponent.ResolveBrush("LayerFillColorAltBrush"))
            .WithBorder(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"), 1)
            .CornerRadius(8)
            .Padding(4)
            .HAlign(horizontal)
            .VAlign(vertical);
    }

    // ---- Zoom controls ----

    private Element BuildZoomControls(
        Microsoft.UI.Reactor.Core.Ref<ScrollViewer?> scrollViewerRef,
        IReadOnlyDictionary<string, InfiniteCanvasNodePosition> positions,
        IReadOnlyDictionary<string, InfiniteCanvasNode> nodes,
        InfiniteCanvasOptions options)
    {
        // Floating icon buttons stacked as a single connected toolbar group: a bordered container
        // holds borderless square buttons with zero spacing, anchored bottom-left with a small margin.
        return Border(
                (VStack(0,
                    ZoomIconButton(HostIconSources.ControlZoomIn, "Zoom in", () => ZoomBy(scrollViewerRef.Current, 1.2, options)),
                    ZoomIconButton(HostIconSources.ControlZoomOut, "Zoom out", () => ZoomBy(scrollViewerRef.Current, 1 / 1.2, options)),
                    ZoomIconButton(HostIconSources.ControlFitView, "Fit to content", () => FitToContent(scrollViewerRef.Current, positions, nodes, options)),
                    ZoomIconButton(HostIconSources.ControlActualSize, "Actual size", () => ResetZoom(scrollViewerRef.Current)))
                .Set(panel => panel.Spacing = 0)))
            .Background(ReactorMainShellComponent.ResolveBrush("LayerFillColorDefaultBrush"))
            .WithBorder(ReactorMainShellComponent.ResolveBrush("CardStrokeColorDefaultBrush"), 1)
            .CornerRadius(8)
            .Padding(0)
            .HAlign(HorizontalAlignment.Left)
            .VAlign(VerticalAlignment.Bottom)
            .Margin(16, 0, 0, 16);
    }

    private static Element ZoomIconButton(string iconSource, string automationName, Action onClick)
    {
        // Borderless, transparent square button so the buttons merge into the container group with no
        // visible gaps; hover/press still highlights the individual button.
        return Button(Image(iconSource).Width(18).Height(18).AccessibilityHidden(), onClick)
            .SubtleButton()
            .AutomationName(automationName)
            .Width(40)
            .Height(40)
            .CornerRadius(0)
            .Set(button => button.Margin = new Thickness(0));
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
        IReadOnlyDictionary<string, InfiniteCanvasNode> nodes,
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
        foreach ((string id, InfiniteCanvasNode node) in nodes)
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
