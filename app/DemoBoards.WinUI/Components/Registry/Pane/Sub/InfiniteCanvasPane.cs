using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.Controls;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;
using DemoBoards_WinUI;
using Microsoft.UI;
using Windows.UI;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Props for <see cref="InfiniteCanvasPane"/>. Mirrors the frontend
/// <c>InfiniteCanvasPane({ boardId, cardIds, cardContents, cardRuntimes, dataObjects, rendererRules })</c>.
/// The WinUI <see cref="BoardCard"/> already merges definition + runtime, so the frontend's separate
/// <c>cardRuntimes</c> map collapses into <see cref="CardContents"/> (status read off the card).
/// </summary>
public sealed record InfiniteCanvasPaneProps(
    string BoardId,
    IReadOnlyList<string> CardIds,
    IReadOnlyDictionary<string, BoardCard> CardContents,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<RendererRule>? RendererRules = null);

/// <summary>
/// Faithful, self-contained port of <c>components/registry/pane/sub/InfiniteCanvasPane.jsx</c>, shaped
/// to drive the shared <see cref="InfiniteCanvas"/> control through its frozen seven-prop contract.
/// <para>
/// It owns its own <see cref="BuildGraph"/> (cards + token edges + incoming/outgoing adjacency) and its
/// own <see cref="BuildDeterministicCanvasLayout"/> (a re-port of <c>lib/boardCanvasLayout.js</c>) — it
/// does NOT depend on <c>BoardCanvasLayoutEngine</c>. Topology is declared on provide ports as
/// <c>links</c>; the canvas derives the edge layer internally (it has no <c>edges</c> prop). Geometry is
/// seeded via <c>GetInitialNodePos</c> and round-tripped through the opaque <c>CanvasState</c> blob.
/// </para>
/// <para>
/// Intentional platform-difference drops (DOM / ReactFlow-imperative only): the floating token banner
/// overlay (no z-layer slot; token selection is still cleared by re-clicking the selected gem), the
/// imperative <c>fitView</c> / token-refit / card-focus effects (the WinUI canvas owns its viewport via
/// the blob and exposes no imperative ref API), and the SVG <c>LeaderLineEdge</c> bezier/marker chrome
/// (the canvas renders its own edge layer from the declared links).
/// </para>
/// </summary>
public sealed class InfiniteCanvasPane : HookComponent<InfiniteCanvasPaneProps>
{
    private const double NodeWidth = 360;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The canvas graph is intentionally assembled from runtime card topology and rendered as dynamic JSON.")]
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        // Card-tier registry must be live before any node routes through CardRenderer.
        RegistryBootstrap.EnsureRegistered();

        string boardId = Props.BoardId;
        var (selectedToken, setSelectedToken) = UseState<string?>(null);
        BoardVisuals visualsHook = UseBoardVisuals(boardId);
        BoardVisualState visuals = visualsHook.Visuals;
        BoardCanvasLayoutState layoutState = visuals.LayoutState;
        BoardLayoutActions actions = visualsHook.Actions;
        IReadOnlyList<string> cardIds = Props.CardIds;
        IReadOnlyDictionary<string, BoardCard> cardContents = Props.CardContents;
        IReadOnlyDictionary<string, string> dataObjects = Props.DataObjects
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<RendererRule>? rendererRules = Props.RendererRules;

        CanvasGraphResult graph = BuildGraph(cardIds, cardContents, dataObjects);
        IReadOnlyDictionary<string, LayoutPlacement> baseLayout = BuildDeterministicCanvasLayout(
            cardIds, cardContents, graph.Incoming, graph.Outgoing, layoutState.Positions, layoutState.Widths);

        var availableTokens = new HashSet<string>(dataObjects.Keys, StringComparer.Ordinal);

        // Nodes that touch the selected token (require or provide). Drives node highlight/dim.
        var highlightedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (selectedToken is not null)
        {
            foreach (string cardId in cardIds)
            {
                if (graph.Cards.TryGetValue(cardId, out GraphCard? card)
                    && (card.Requires.Contains(selectedToken) || card.Provides.Contains(selectedToken)))
                {
                    highlightedNodeIds.Add(cardId);
                }
            }
        }

        // Toggle token focus. Closes over this render's selectedToken (Reactor setters take a value).
        void HandleTokenToggle(string token) =>
            setSelectedToken(string.Equals(selectedToken, token, StringComparison.Ordinal) ? null : token);

        // ---- Build the opaque node/port/link graph JSON, then split into the canvas's two props. ----
        var nodeList = new List<object>(cardIds.Count);
        var portsMap = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (string cardId in cardIds)
        {
            if (!graph.Cards.TryGetValue(cardId, out GraphCard? card))
            {
                continue;
            }

            bool highlighted = selectedToken is null || highlightedNodeIds.Contains(cardId);
            bool dimmed = selectedToken is not null && !highlightedNodeIds.Contains(cardId);
            double width = layoutState.Widths.TryGetValue(cardId, out double storedWidth) && double.IsFinite(storedWidth)
                ? storedWidth
                : baseLayout.TryGetValue(cardId, out LayoutPlacement? placement)
                    ? placement.W
                    : NodeWidth;

            nodeList.Add(new
            {
                id = cardId,
                status = card.Status,
                highlighted,
                dimmed,
                width,
            });

            bool isRunningCard = string.Equals(card.Status, "running", StringComparison.Ordinal);

            var top = card.Requires.Select(token => (object)new
            {
                id = $"require:{token}",
                variant = "require",
                token,
                handleType = "target",
                title = $"Requires {token}",
                missing = !availableTokens.Contains(token),
                selected = string.Equals(selectedToken, token, StringComparison.Ordinal),
                running = isRunningCard,
            }).ToList();

            var bottom = card.Provides.Select(token =>
            {
                var links = graph.Edges
                    .Where(edge => string.Equals(edge.Source, cardId, StringComparison.Ordinal)
                        && string.Equals(edge.Token, token, StringComparison.Ordinal))
                    .Select(edge => (object)new
                    {
                        target = edge.Target,
                        port = $"require:{token}",
                        label = token,
                        animated = edge.Animated,
                    })
                    .ToList();

                return (object)new
                {
                    id = $"provide:{token}",
                    variant = "provide",
                    token,
                    handleType = "source",
                    title = $"Provides {token}",
                    active = card.ProvidesActive.Contains(token),
                    selected = string.Equals(selectedToken, token, StringComparison.Ordinal),
                    links,
                };
            }).ToList();

            portsMap[cardId] = new { top, bottom };
        }

        JsonElement graphJson = JsonSerializer.SerializeToElement(new { nodes = nodeList, ports = portsMap });
        InfiniteCanvasGraph canvasGraph = InfiniteCanvasGraph.FromJson(graphJson);

        // ---- Opaque CanvasState blob: owned by InfiniteCanvas and round-tripped verbatim. ----
        JsonElement? canvasState = layoutState.InfiniteCanvasBlob;

        // ---- Canvas callbacks (closures over this render's state). ----
        Element RenderNode(JsonElement node)
        {
            string id = node.GetProperty("id").GetString()!;
            bool dimmed = node.TryGetProperty("dimmed", out JsonElement d) && d.ValueKind == JsonValueKind.True;
            Element rendered = Component<CardRenderer, CardRendererProps>(
                new CardRendererProps(boardId, id, rendererRules, EnableResize: true, Chrome: "full"));
            return dimmed ? rendered.Opacity(0.45) : rendered;
        }

        Element RenderNodePort(JsonElement port, InfiniteCanvasPortRenderContext ctx)
        {
            string variant = ReadString(port, "variant") ?? "require";
            string token = ReadString(port, "token") ?? port.GetProperty("id").GetString()!;
            bool provide = string.Equals(variant, "provide", StringComparison.Ordinal);
            bool selected = port.TryGetProperty("selected", out JsonElement s) && s.ValueKind == JsonValueKind.True;
            bool active = port.TryGetProperty("active", out JsonElement a) && a.ValueKind == JsonValueKind.True;
            return BuildTokenGem(theme, token, provide, selected, active, () => HandleTokenToggle(token));
        }

        InfiniteCanvasNodeGeometry? GetInitialNodePos(InfiniteCanvasNodePlacement placement)
        {
            string id = placement.Node.GetProperty("id").GetString()!;
            return baseLayout.TryGetValue(id, out LayoutPlacement? p)
                ? new InfiniteCanvasNodeGeometry(p.X, p.Y, p.W, p.H)
                : null;
        }

        void HandleCanvasStateCommit(JsonElement blob)
        {
            actions.SetInfiniteCanvasBlob(blob);
            actions.ScheduleAutosave();
        }

        return Component<InfiniteCanvas, InfiniteCanvasProps>(
            new InfiniteCanvasProps(
                Nodes: canvasGraph.Nodes,
                RenderNode: RenderNode,
                NodePorts: canvasGraph.NodePorts,
                RenderNodePort: RenderNodePort,
                GetInitialNodePos: GetInitialNodePos,
                CanvasState: canvasState,
                OnCanvasStateCommit: HandleCanvasStateCommit,
                StateKey: boardId,
                Options: new InfiniteCanvasOptions(
                    MinZoom: 0.24,
                    MaxZoom: 1.35,
                    ShowGrid: true,
                    MiniMap: InfiniteCanvasMiniMapPlacement.BottomRight,
                    ShowZoomControls: true,
                    GridSpacing: 24)));
    }

    private static Element BuildTokenGem(AppTheme theme, string token, bool provide, bool selected, bool active, Action onClick) =>
        Button(token, onClick)
            .AutomationName($"{(provide ? "Provides" : "Requires")} {token}")
            .CornerRadius(10)
            .Padding(8, 2, 8, 2)
            .MinWidth(0)
            .Background(selected || (provide && active)
                ? theme.Accent
                : theme.ControlFill)
            .WithBorder(theme.CardBorder, 1)
            .Set(button => button.FontSize = 10);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // =====================================================================================
    //  Pure graph + layout (port of buildGraph + lib/boardCanvasLayout.js). Internal so the
    //  pure-logic harness can exercise them without instantiating the Reactor component.
    // =====================================================================================

    internal sealed record GraphCard(
        string Id,
        string Title,
        string Status,
        IReadOnlyList<string> Requires,
        IReadOnlyList<string> Provides,
        IReadOnlyList<string> ProvidesActive);

    internal sealed record GraphEdge(string Id, string Source, string Target, string Token, bool Animated);

    internal sealed record CanvasGraphResult(
        IReadOnlyDictionary<string, GraphCard> Cards,
        IReadOnlyList<GraphEdge> Edges,
        IReadOnlyDictionary<string, HashSet<string>> Incoming,
        IReadOnlyDictionary<string, HashSet<string>> Outgoing);

    internal sealed record LayoutPlacement(double X, double Y, double W, double H);

    private sealed record LayoutDescriptor(
        string CardId,
        string Title,
        int Prominence,
        double Width,
        double Height,
        BoardCanvasPointState? StoredPosition,
        double? StoredWidth);

    private static readonly IReadOnlyDictionary<string, double> FootprintWidths =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["compact"] = 300,
            ["standard"] = 360,
            ["wide"] = 440,
            ["large"] = 520,
        };

    private static readonly IReadOnlyDictionary<string, int> ProminenceOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["spotlight"] = 0,
            ["feature"] = 1,
            ["standard"] = 2,
            ["glance"] = 3,
        };

    /// <summary>Maps a runtime status to its tone class (port of <c>getStatusTone</c>).</summary>
    internal static string GetStatusTone(string? status) => status switch
    {
        "completed" => "board-tone--completed",
        "running" => "board-tone--running",
        "failed" => "board-tone--failed",
        "blocked" => "board-tone--blocked",
        _ => "board-tone--fresh",
    };

    /// <summary>De-duplicates a token list, dropping empties and preserving first-seen order.</summary>
    internal static List<string> UniqueTokens(IEnumerable<string>? tokens)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        if (tokens is null)
        {
            return result;
        }

        foreach (string token in tokens)
        {
            if (!string.IsNullOrEmpty(token) && seen.Add(token))
            {
                result.Add(token);
            }
        }

        return result;
    }

    /// <summary>Port of <c>buildGraph</c>: cards, token edges, and incoming/outgoing adjacency.</summary>
    internal static CanvasGraphResult BuildGraph(
        IReadOnlyList<string> cardIds,
        IReadOnlyDictionary<string, BoardCard> cardContents,
        IReadOnlyDictionary<string, string> dataObjects)
    {
        var visibleIds = new HashSet<string>(cardIds, StringComparer.Ordinal);
        var cards = new Dictionary<string, GraphCard>(StringComparer.Ordinal);
        var tokenProviders = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string cardId in cardIds)
        {
            cardContents.TryGetValue(cardId, out BoardCard? card);
            string status = string.IsNullOrEmpty(card?.Status) ? "fresh" : card!.Status;
            List<string> requires = UniqueTokens(card?.Requires);
            List<string> provides = UniqueTokens(card?.Provides);
            List<string> providesActive = provides.Where(dataObjects.ContainsKey).ToList();

            cards[cardId] = new GraphCard(cardId, ResolveTitle(card, cardId), status, requires, provides, providesActive);

            foreach (string token in provides)
            {
                if (!tokenProviders.TryGetValue(token, out List<string>? providers))
                {
                    providers = new List<string>();
                    tokenProviders[token] = providers;
                }

                providers.Add(cardId);
            }
        }

        var edges = new List<GraphEdge>();
        var incoming = cardIds.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var outgoing = cardIds.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (string cardId in cardIds)
        {
            GraphCard card = cards[cardId];
            foreach (string token in card.Requires)
            {
                if (!tokenProviders.TryGetValue(token, out List<string>? providers))
                {
                    continue;
                }

                foreach (string sourceId in providers)
                {
                    if (string.Equals(sourceId, cardId, StringComparison.Ordinal) || !visibleIds.Contains(sourceId))
                    {
                        continue;
                    }

                    bool isRunningEdge = string.Equals(card.Status, "running", StringComparison.Ordinal);
                    edges.Add(new GraphEdge($"{sourceId}::{cardId}::{token}", sourceId, cardId, token, isRunningEdge));
                    incoming[cardId].Add(sourceId);
                    outgoing[sourceId].Add(cardId);
                }
            }
        }

        return new CanvasGraphResult(cards, edges, incoming, outgoing);
    }

    /// <summary>
    /// Port of <c>buildDeterministicCanvasLayout</c>. Returns placements for UNSAVED cards only — cards
    /// with a stored position are seeded from the persisted blob, so they are intentionally absent here
    /// (matching the frontend). Stored cards still participate as occupied space / layout anchors.
    /// </summary>
    internal static IReadOnlyDictionary<string, LayoutPlacement> BuildDeterministicCanvasLayout(
        IReadOnlyList<string> cardIds,
        IReadOnlyDictionary<string, BoardCard> cardContents,
        IReadOnlyDictionary<string, HashSet<string>> incoming,
        IReadOnlyDictionary<string, HashSet<string>> outgoing,
        IReadOnlyDictionary<string, BoardCanvasPointState> storedPositions,
        IReadOnlyDictionary<string, double> storedWidths)
    {
        BoardCanvasLayoutDefaults config = BoardCanvasLayoutDefaults.Default;
        var placements = new Dictionary<string, LayoutPlacement>(StringComparer.Ordinal);
        var occupiedRects = new List<LayoutPlacement>();
        var positionedRectsById = new Dictionary<string, LayoutPlacement>(StringComparer.Ordinal);

        LayoutDescriptor[] descriptors = cardIds
            .Select(cardId => BuildLayoutDescriptor(cardId, cardContents.GetValueOrDefault(cardId), storedPositions, storedWidths, config))
            .ToArray();
        var descriptorsById = descriptors.ToDictionary(descriptor => descriptor.CardId, descriptor => descriptor, StringComparer.Ordinal);

        foreach (LayoutDescriptor descriptor in descriptors.Where(item => item.StoredPosition is not null))
        {
            var rect = new LayoutPlacement(
                descriptor.StoredPosition!.X,
                descriptor.StoredPosition.Y,
                descriptor.StoredWidth ?? descriptor.Width,
                descriptor.Height);
            occupiedRects.Add(rect);
            positionedRectsById[descriptor.CardId] = rect;
        }

        string[] unsavedIds = descriptors
            .Where(descriptor => descriptor.StoredPosition is null)
            .Select(descriptor => descriptor.CardId)
            .ToArray();
        Dictionary<string, HashSet<string>> unsavedIncoming = RestrictAdjacency(unsavedIds, incoming);
        Dictionary<string, HashSet<string>> unsavedOutgoing = RestrictAdjacency(unsavedIds, outgoing);
        Dictionary<string, int> depthMap = BuildDepthMap(unsavedIds, unsavedIncoming, unsavedOutgoing);

        List<List<string>> components = BuildWeaklyConnectedComponents(unsavedIds, unsavedIncoming, unsavedOutgoing)
            .OrderBy(component => component.Min(cardId => depthMap.GetValueOrDefault(cardId, 0)))
            .ThenBy(
                component => component.Select(cardId => descriptorsById[cardId].Title).OrderBy(title => title, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty,
                StringComparer.InvariantCulture)
            .ToList();

        foreach (List<string> componentIds in components)
        {
            Dictionary<string, HashSet<string>> componentIncoming = RestrictAdjacency(componentIds, unsavedIncoming);
            Dictionary<string, HashSet<string>> componentOutgoing = RestrictAdjacency(componentIds, unsavedOutgoing);
            Dictionary<string, int> componentDepthMap = BuildDepthMap(componentIds, componentIncoming, componentOutgoing);
            var columnY = new Dictionary<int, double>();
            var componentPlacements = new Dictionary<string, LayoutPlacement>(StringComparer.Ordinal);

            foreach (LayoutDescriptor descriptor in Order(componentIds, descriptorsById, componentDepthMap))
            {
                int column = componentDepthMap.GetValueOrDefault(descriptor.CardId, 0);
                double y = columnY.GetValueOrDefault(column, 0);
                componentPlacements[descriptor.CardId] = new LayoutPlacement(
                    column * config.ColumnGap,
                    y,
                    descriptor.Width,
                    descriptor.Height);
                columnY[column] = y + descriptor.Height + config.RowGap;
            }

            (double width, double height) bounds = MeasureComponentLayout(componentPlacements.Values);
            BoardCanvasPointState? preferredOrigin = ResolvePreferredOrigin(componentIds, bounds.width, bounds.height, incoming, outgoing, positionedRectsById);
            LayoutPlacement anchor = FindOpenPosition(bounds.width, bounds.height, occupiedRects, preferredOrigin, config);

            foreach ((string cardId, LayoutPlacement placement) in componentPlacements)
            {
                var absolute = new LayoutPlacement(
                    anchor.X + placement.X,
                    anchor.Y + placement.Y,
                    placement.W,
                    placement.H);
                placements[cardId] = absolute;
                occupiedRects.Add(absolute);
                positionedRectsById[cardId] = absolute;
            }
        }

        return placements;
    }

    private static IEnumerable<LayoutDescriptor> Order(
        IReadOnlyList<string> componentIds,
        IReadOnlyDictionary<string, LayoutDescriptor> descriptorsById,
        IReadOnlyDictionary<string, int> componentDepthMap) =>
        componentIds
            .Select(cardId => descriptorsById[cardId])
            .OrderBy(descriptor => componentDepthMap.GetValueOrDefault(descriptor.CardId, 0))
            .ThenBy(descriptor => descriptor.Prominence)
            .ThenBy(descriptor => descriptor.Title, StringComparer.InvariantCulture);

    private static LayoutDescriptor BuildLayoutDescriptor(
        string cardId,
        BoardCard? card,
        IReadOnlyDictionary<string, BoardCanvasPointState> storedPositions,
        IReadOnlyDictionary<string, double> storedWidths,
        BoardCanvasLayoutDefaults config)
    {
        (string? prominence, string? footprint, double? legacyHeight) = ReadPresentation(card?.RawDefinitionJson);
        // JS trims before the (case-sensitive) lookup: FOOTPRINT_WIDTH[footprint.trim()] / PROMINENCE_ORDER[prominence.trim()].
        string? footprintKey = footprint?.Trim();
        double width = footprintKey is not null && FootprintWidths.TryGetValue(footprintKey, out double footprintWidth)
            ? footprintWidth
            : config.DefaultCardWidth;
        double height = legacyHeight is > 0 ? legacyHeight.Value : config.DefaultCardHeight;
        string? prominenceKey = prominence?.Trim();
        int prominenceRank = prominenceKey is not null && ProminenceOrder.TryGetValue(prominenceKey, out int prominenceWeight)
            ? prominenceWeight
            : ProminenceOrder["standard"];
        BoardCanvasPointState? stored = storedPositions.TryGetValue(cardId, out BoardCanvasPointState? point) ? point : null;
        double? storedWidth = storedWidths.TryGetValue(cardId, out double width2) && width2 > 0 ? width2 : null;

        return new LayoutDescriptor(cardId, ResolveTitle(card, cardId), prominenceRank, width, height, stored, storedWidth);
    }

    private static string ResolveTitle(BoardCard? card, string cardId)
    {
        if (card?.MetaValues is { } meta && meta.TryGetValue("title", out string? title) && title is not null)
        {
            return title;
        }

        return cardId;
    }

    private static (string? Prominence, string? Footprint, double? LegacyHeight) ReadPresentation(string? rawDefinitionJson)
    {
        if (string.IsNullOrWhiteSpace(rawDefinitionJson))
        {
            return (null, null, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawDefinitionJson);
            JsonElement root = document.RootElement;
            JsonElement presentation = root.TryGetProperty("meta", out JsonElement metaElement)
                && metaElement.ValueKind == JsonValueKind.Object
                && metaElement.TryGetProperty("presentation", out JsonElement presentationElement)
                && presentationElement.ValueKind == JsonValueKind.Object
                ? presentationElement
                : default;
            string? prominence = presentation.ValueKind == JsonValueKind.Object
                && presentation.TryGetProperty("prominence", out JsonElement prominenceElement)
                && prominenceElement.ValueKind == JsonValueKind.String
                ? prominenceElement.GetString()
                : null;
            string? footprint = presentation.ValueKind == JsonValueKind.Object
                && presentation.TryGetProperty("footprint", out JsonElement footprintElement)
                && footprintElement.ValueKind == JsonValueKind.String
                ? footprintElement.GetString()
                : null;
            double? legacyHeight = root.TryGetProperty("view", out JsonElement viewElement)
                && viewElement.ValueKind == JsonValueKind.Object
                && viewElement.TryGetProperty("layout", out JsonElement layoutElement)
                && layoutElement.ValueKind == JsonValueKind.Object
                && layoutElement.TryGetProperty("canvas", out JsonElement canvasElement)
                && canvasElement.ValueKind == JsonValueKind.Object
                && canvasElement.TryGetProperty("h", out JsonElement heightElement)
                && heightElement.ValueKind == JsonValueKind.Number
                ? heightElement.GetDouble()
                : null;
            return (prominence, footprint, legacyHeight);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static Dictionary<string, HashSet<string>> RestrictAdjacency(
        IEnumerable<string> allowedCardIds,
        IReadOnlyDictionary<string, HashSet<string>> adjacency)
    {
        var allowed = new HashSet<string>(allowedCardIds, StringComparer.Ordinal);
        return allowed.ToDictionary(
            cardId => cardId,
            cardId => new HashSet<string>(
                (adjacency.TryGetValue(cardId, out HashSet<string>? neighbors) ? neighbors : Enumerable.Empty<string>()).Where(allowed.Contains),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, int> BuildDepthMap(
        IEnumerable<string> cardIds,
        IReadOnlyDictionary<string, HashSet<string>> incoming,
        IReadOnlyDictionary<string, HashSet<string>> outgoing)
    {
        string[] ids = cardIds.ToArray();
        var indegree = ids.ToDictionary(cardId => cardId, cardId => incoming.GetValueOrDefault(cardId)?.Count ?? 0, StringComparer.Ordinal);
        var depth = ids.ToDictionary(cardId => cardId, _ => 0, StringComparer.Ordinal);
        var queue = new Queue<string>(ids.Where(cardId => indegree[cardId] == 0));
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            string cardId = queue.Dequeue();
            visited.Add(cardId);
            int nextDepth = depth[cardId] + 1;
            foreach (string nextId in outgoing.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
            {
                depth[nextId] = Math.Max(depth.GetValueOrDefault(nextId, 0), nextDepth);
                int remaining = indegree.GetValueOrDefault(nextId, 0) - 1;
                indegree[nextId] = remaining;
                if (remaining == 0)
                {
                    queue.Enqueue(nextId);
                }
            }
        }

        foreach (string cardId in ids)
        {
            if (!visited.Contains(cardId) && !depth.ContainsKey(cardId))
            {
                depth[cardId] = 0;
            }
        }

        return depth;
    }

    private static List<List<string>> BuildWeaklyConnectedComponents(
        IEnumerable<string> cardIds,
        IReadOnlyDictionary<string, HashSet<string>> incoming,
        IReadOnlyDictionary<string, HashSet<string>> outgoing)
    {
        var remaining = new HashSet<string>(cardIds, StringComparer.Ordinal);
        var components = new List<List<string>>();

        while (remaining.Count > 0)
        {
            string seedId = remaining.First();
            var queue = new Queue<string>();
            queue.Enqueue(seedId);
            remaining.Remove(seedId);
            var component = new List<string>();

            while (queue.Count > 0)
            {
                string cardId = queue.Dequeue();
                component.Add(cardId);

                foreach (string neighborId in incoming.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
                {
                    if (remaining.Remove(neighborId))
                    {
                        queue.Enqueue(neighborId);
                    }
                }

                foreach (string neighborId in outgoing.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
                {
                    if (remaining.Remove(neighborId))
                    {
                        queue.Enqueue(neighborId);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static (double Width, double Height) MeasureComponentLayout(IEnumerable<LayoutPlacement> placements)
    {
        double width = 0;
        double height = 0;
        foreach (LayoutPlacement placement in placements)
        {
            width = Math.Max(width, placement.X + placement.W);
            height = Math.Max(height, placement.Y + placement.H);
        }

        return (width, height);
    }

    private static BoardCanvasPointState? ResolvePreferredOrigin(
        IEnumerable<string> componentIds,
        double boundsWidth,
        double boundsHeight,
        IReadOnlyDictionary<string, HashSet<string>> incoming,
        IReadOnlyDictionary<string, HashSet<string>> outgoing,
        IReadOnlyDictionary<string, LayoutPlacement> positionedRectsById)
    {
        var componentSet = new HashSet<string>(componentIds, StringComparer.Ordinal);
        var neighborRects = new List<LayoutPlacement>();

        foreach (string cardId in componentIds)
        {
            foreach (string neighborId in incoming.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
            {
                if (!componentSet.Contains(neighborId)
                    && positionedRectsById.TryGetValue(neighborId, out LayoutPlacement? rect)
                    && rect is not null)
                {
                    neighborRects.Add(rect);
                }
            }

            foreach (string neighborId in outgoing.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
            {
                if (!componentSet.Contains(neighborId)
                    && positionedRectsById.TryGetValue(neighborId, out LayoutPlacement? rect)
                    && rect is not null)
                {
                    neighborRects.Add(rect);
                }
            }
        }

        if (neighborRects.Count == 0)
        {
            return null;
        }

        double centerX = neighborRects.Average(rect => rect.X + (rect.W / 2));
        double centerY = neighborRects.Average(rect => rect.Y + (rect.H / 2));
        return new BoardCanvasPointState(centerX - (boundsWidth / 2), centerY - (boundsHeight / 2));
    }

    private static LayoutPlacement FindOpenPosition(
        double width,
        double height,
        IReadOnlyList<LayoutPlacement> occupiedRects,
        BoardCanvasPointState? preferredOrigin,
        BoardCanvasLayoutDefaults config)
    {
        int maxColumns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(occupiedRects.Count + 1)) + 2);

        if (preferredOrigin is not null)
        {
            int originColumn = Math.Max(0, (int)Math.Round((preferredOrigin.X - config.OriginX) / config.ColumnGap));
            int originRow = Math.Max(0, (int)Math.Round((preferredOrigin.Y - config.OriginY) / config.RowGap));
            int maxRadius = Math.Max(8, maxColumns + 4);

            for (int radius = 0; radius <= maxRadius; radius += 1)
            {
                for (int rowOffset = -radius; rowOffset <= radius; rowOffset += 1)
                {
                    for (int columnOffset = -radius; columnOffset <= radius; columnOffset += 1)
                    {
                        if (Math.Max(Math.Abs(rowOffset), Math.Abs(columnOffset)) != radius)
                        {
                            continue;
                        }

                        int columnIndex = originColumn + columnOffset;
                        int rowIndex = originRow + rowOffset;
                        if (columnIndex < 0 || rowIndex < 0)
                        {
                            continue;
                        }

                        var candidate = new LayoutPlacement(
                            config.OriginX + (columnIndex * config.ColumnGap),
                            config.OriginY + (rowIndex * config.RowGap),
                            width,
                            height);
                        if (!occupiedRects.Any(occupied => RectanglesOverlap(candidate, occupied)))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }

        for (int rowIndex = 0; rowIndex < 200; rowIndex += 1)
        {
            for (int columnIndex = 0; columnIndex < maxColumns; columnIndex += 1)
            {
                var candidate = new LayoutPlacement(
                    config.OriginX + (columnIndex * config.ColumnGap),
                    config.OriginY + (rowIndex * config.RowGap),
                    width,
                    height);
                if (!occupiedRects.Any(occupied => RectanglesOverlap(candidate, occupied)))
                {
                    return candidate;
                }
            }
        }

        return new LayoutPlacement(config.OriginX, config.OriginY, width, height);
    }

    private static bool RectanglesOverlap(LayoutPlacement left, LayoutPlacement right) =>
        left.X < (right.X + right.W)
        && (left.X + left.W) > right.X
        && left.Y < (right.Y + right.H)
        && (left.Y + left.H) > right.Y;
}
