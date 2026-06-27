using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Lib;

public sealed record BoardCanvasPlacement(double X, double Y, double Width, double Height);

public static class BoardCanvasLayoutEngine
{
    private static BoardCanvasLayoutDefaults defaults = BoardCanvasLayoutDefaults.Default;

    private static readonly IReadOnlyDictionary<string, double> FootprintWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["compact"] = 300,
        ["standard"] = 360,
        ["wide"] = 440,
        ["large"] = 520,
    };

    private static readonly IReadOnlyDictionary<string, int> ProminenceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["spotlight"] = 0,
        ["feature"] = 1,
        ["standard"] = 2,
        ["glance"] = 3,
    };

    public static void ConfigureDefaults(BoardCanvasLayoutDefaults config)
    {
        defaults = config ?? BoardCanvasLayoutDefaults.Default;
    }

    public static IReadOnlyDictionary<string, BoardCanvasPlacement> BuildPlacements(IReadOnlyList<BoardCard> cards, BoardCanvasLayoutState storedLayout)
    {
        var incoming = cards.ToDictionary(card => card.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var outgoing = cards.ToDictionary(card => card.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var tokenProviders = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (BoardCard card in cards)
        {
            foreach (string token in card.Provides.Where(token => !string.IsNullOrWhiteSpace(token)))
            {
                if (!tokenProviders.TryGetValue(token, out List<string>? providers))
                {
                    providers = new List<string>();
                    tokenProviders[token] = providers;
                }

                providers.Add(card.Id);
            }
        }

        foreach (BoardCard card in cards)
        {
            foreach (string token in card.Requires.Where(token => !string.IsNullOrWhiteSpace(token)))
            {
                if (!tokenProviders.TryGetValue(token, out List<string>? providers))
                {
                    continue;
                }

                foreach (string providerId in providers)
                {
                    if (string.Equals(providerId, card.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    incoming[card.Id].Add(providerId);
                    outgoing[providerId].Add(card.Id);
                }
            }
        }

        var descriptors = cards
            .Select(card => BuildDescriptor(card, storedLayout))
            .ToArray();
        var descriptorsById = descriptors.ToDictionary(descriptor => descriptor.CardId, descriptor => descriptor, StringComparer.Ordinal);
        var placements = new Dictionary<string, BoardCanvasPlacement>(StringComparer.Ordinal);
        var occupiedRects = new List<BoardCanvasPlacement>();
        var positionedRectsById = new Dictionary<string, BoardCanvasPlacement>(StringComparer.Ordinal);

        foreach (CardLayoutDescriptor descriptor in descriptors.Where(item => item.StoredPosition is not null))
        {
            BoardCanvasPlacement rect = new(
                descriptor.StoredPosition!.X,
                descriptor.StoredPosition.Y,
                descriptor.StoredWidth ?? descriptor.Width,
                descriptor.Height);
            placements[descriptor.CardId] = rect;
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
            .ThenBy(component => component.Select(cardId => descriptorsById[cardId].Title).OrderBy(title => title, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (List<string> componentIds in components)
        {
            Dictionary<string, HashSet<string>> componentIncoming = RestrictAdjacency(componentIds, unsavedIncoming);
            Dictionary<string, HashSet<string>> componentOutgoing = RestrictAdjacency(componentIds, unsavedOutgoing);
            Dictionary<string, int> componentDepthMap = BuildDepthMap(componentIds, componentIncoming, componentOutgoing);
            var columnY = new Dictionary<int, double>();
            var componentPlacements = new Dictionary<string, BoardCanvasPlacement>(StringComparer.Ordinal);

            foreach (CardLayoutDescriptor descriptor in componentIds
                .Select(cardId => descriptorsById[cardId] with { Column = componentDepthMap.GetValueOrDefault(cardId, 0) })
                .OrderBy(item => item.Column)
                .ThenBy(item => item.Prominence)
                .ThenBy(item => item.Title, StringComparer.Ordinal))
            {
                double y = columnY.GetValueOrDefault(descriptor.Column, 0);
                componentPlacements[descriptor.CardId] = new BoardCanvasPlacement(
                    descriptor.Column * defaults.ColumnGap,
                    y,
                    descriptor.Width,
                    descriptor.Height);
                columnY[descriptor.Column] = y + descriptor.Height + defaults.RowGap;
            }

            (double width, double height) bounds = MeasureComponentLayout(componentPlacements.Values);
            BoardCanvasPointState? preferredOrigin = ResolvePreferredOrigin(componentIds, bounds.width, bounds.height, incoming, outgoing, positionedRectsById);
            BoardCanvasPlacement anchor = FindOpenPosition(bounds.width, bounds.height, occupiedRects, preferredOrigin);

            foreach ((string cardId, BoardCanvasPlacement placement) in componentPlacements)
            {
                BoardCanvasPlacement absolute = new(
                    anchor.X + placement.X,
                    anchor.Y + placement.Y,
                    placement.Width,
                    placement.Height);
                placements[cardId] = absolute;
                occupiedRects.Add(absolute);
                positionedRectsById[cardId] = absolute;
            }
        }

        return placements;
    }

    private static CardLayoutDescriptor BuildDescriptor(BoardCard card, BoardCanvasLayoutState storedLayout)
    {
        (string? prominence, string? footprint, double? legacyHeight) presentation = ReadPresentation(card.RawDefinitionJson);
        double width = presentation.footprint is not null && FootprintWidths.TryGetValue(presentation.footprint, out double footprintWidth)
            ? footprintWidth
            : defaults.DefaultCardWidth;
        double height = presentation.legacyHeight is > 0 ? presentation.legacyHeight.Value : defaults.DefaultCardHeight;
        int prominence = presentation.prominence is not null && ProminenceOrder.TryGetValue(presentation.prominence, out int prominenceWeight)
            ? prominenceWeight
            : 2;

        return new CardLayoutDescriptor(
            card.Id,
            string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title,
            prominence,
            width,
            height,
            storedLayout.Positions.TryGetValue(card.Id, out BoardCanvasPointState? point) ? point : null,
            storedLayout.Widths.TryGetValue(card.Id, out double storedWidth) && storedWidth > 0 ? storedWidth : null,
            0);
    }

    private static (string? prominence, string? footprint, double? legacyHeight) ReadPresentation(string rawDefinitionJson)
    {
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
        catch
        {
            return (null, null, null);
        }
    }

    private static Dictionary<string, HashSet<string>> RestrictAdjacency(IEnumerable<string> allowedCardIds, IReadOnlyDictionary<string, HashSet<string>> adjacency)
    {
        HashSet<string> allowed = new(allowedCardIds, StringComparer.Ordinal);
        return allowed.ToDictionary(
            cardId => cardId,
            cardId => new HashSet<string>((adjacency.TryGetValue(cardId, out HashSet<string>? neighbors) ? neighbors : Enumerable.Empty<string>()).Where(allowed.Contains), StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, int> BuildDepthMap(IEnumerable<string> cardIds, IReadOnlyDictionary<string, HashSet<string>> incoming, IReadOnlyDictionary<string, HashSet<string>> outgoing)
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

    private static List<List<string>> BuildWeaklyConnectedComponents(IEnumerable<string> cardIds, IReadOnlyDictionary<string, HashSet<string>> incoming, IReadOnlyDictionary<string, HashSet<string>> outgoing)
    {
        HashSet<string> remaining = new(cardIds, StringComparer.Ordinal);
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

    private static (double width, double height) MeasureComponentLayout(IEnumerable<BoardCanvasPlacement> placements)
    {
        double width = 0;
        double height = 0;
        foreach (BoardCanvasPlacement placement in placements)
        {
            width = Math.Max(width, placement.X + placement.Width);
            height = Math.Max(height, placement.Y + placement.Height);
        }

        return (width, height);
    }

    private static BoardCanvasPointState? ResolvePreferredOrigin(
        IEnumerable<string> componentIds,
        double boundsWidth,
        double boundsHeight,
        IReadOnlyDictionary<string, HashSet<string>> incoming,
        IReadOnlyDictionary<string, HashSet<string>> outgoing,
        IReadOnlyDictionary<string, BoardCanvasPlacement> positionedRectsById)
    {
        HashSet<string> componentSet = new(componentIds, StringComparer.Ordinal);
        var neighborRects = new List<BoardCanvasPlacement>();

        foreach (string cardId in componentIds)
        {
            foreach (string neighborId in incoming.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
            {
                if (!componentSet.Contains(neighborId)
                    && positionedRectsById.TryGetValue(neighborId, out BoardCanvasPlacement? rect)
                    && rect is not null)
                {
                    neighborRects.Add(rect);
                }
            }

            foreach (string neighborId in outgoing.GetValueOrDefault(cardId) ?? Enumerable.Empty<string>())
            {
                if (!componentSet.Contains(neighborId)
                    && positionedRectsById.TryGetValue(neighborId, out BoardCanvasPlacement? rect)
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

        double centerX = neighborRects.Average(rect => rect.X + (rect.Width / 2));
        double centerY = neighborRects.Average(rect => rect.Y + (rect.Height / 2));
        return new BoardCanvasPointState(centerX - (boundsWidth / 2), centerY - (boundsHeight / 2));
    }

    private static BoardCanvasPlacement FindOpenPosition(double width, double height, IReadOnlyList<BoardCanvasPlacement> occupiedRects, BoardCanvasPointState? preferredOrigin)
    {
        int maxColumns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(occupiedRects.Count + 1)) + 2);

        if (preferredOrigin is not null)
        {
            int originColumn = Math.Max(0, (int)Math.Round((preferredOrigin.X - defaults.OriginX) / defaults.ColumnGap));
            int originRow = Math.Max(0, (int)Math.Round((preferredOrigin.Y - defaults.OriginY) / defaults.RowGap));
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

                        BoardCanvasPlacement candidate = new(
                            defaults.OriginX + (columnIndex * defaults.ColumnGap),
                            defaults.OriginY + (rowIndex * defaults.RowGap),
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
                BoardCanvasPlacement candidate = new(
                    defaults.OriginX + (columnIndex * defaults.ColumnGap),
                    defaults.OriginY + (rowIndex * defaults.RowGap),
                    width,
                    height);
                if (!occupiedRects.Any(occupied => RectanglesOverlap(candidate, occupied)))
                {
                    return candidate;
                }
            }
        }

        return new BoardCanvasPlacement(defaults.OriginX, defaults.OriginY, width, height);
    }

    private static bool RectanglesOverlap(BoardCanvasPlacement left, BoardCanvasPlacement right)
    {
        return left.X < (right.X + right.Width)
            && (left.X + left.Width) > right.X
            && left.Y < (right.Y + right.Height)
            && (left.Y + left.Height) > right.Y;
    }

    private sealed record CardLayoutDescriptor(
        string CardId,
        string Title,
        int Prominence,
        double Width,
        double Height,
        BoardCanvasPointState? StoredPosition,
        double? StoredWidth,
        int Column);
}