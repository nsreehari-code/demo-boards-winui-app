using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

/// <summary>Stable layout action callbacks (port of <c>useBoardLayoutActions</c>).</summary>
public sealed record BoardLayoutActions(
    Action<string, double, double> SetCoords,
    Action<IReadOnlyDictionary<string, BoardCanvasPointState>> SetManyCoords,
    Action<string, double> SetWidth,
    Action<double, double, double> SetViewport,
    Action<JsonElement> SetInfiniteCanvasBlob,
    Func<Task> FlushLayout,
    Action ScheduleAutosave);

/// <summary>
/// Visual board state: resolved UI config, persisted layout blob, live canvas layout slice, and
/// layout persistence state.
/// </summary>
public sealed record BoardVisualState(
    JsonObject Ui,
    JsonObject LayoutBlob,
    string Theme,
    string CentrePaneKind,
    BoardCanvasLayoutState LayoutState);

/// <summary>
/// Visual hook result: current visual state plus shallow-merge and layout actions.
/// </summary>
public sealed record BoardVisuals(
    BoardVisualState Visuals,
    Func<string, JsonNode?, Task<JsonObject?>> ShallowMerge,
    BoardLayoutActions Actions);

public abstract partial class HookComponent<TProps>
{
    private const string DefaultCentrePaneKind = "infinite-canvas";

    /// <summary>
    /// Reads the current board's visual config and canvas state from BoardStore and exposes the
    /// layout autosave actions. Layout persistence uses the <c>shallow-merge</c> manage-boards
    /// subcommand so non-canvas visual keys inside the layout blob survive canvas saves.
    /// </summary>
    protected BoardVisuals UseBoardVisuals(string boardId)
    {
        const string winUiLayoutNamespace = "winui";
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        EmbeddedBoardClient client = UseEmbeddedClient();
        var autosaveTimerRef = UseRef<Timer?>(null);

        string currentBoardId = store.GetBoardInfo().BoardId;
        string targetBoardId = string.IsNullOrWhiteSpace(boardId) ? currentBoardId : boardId.Trim();
        ManagedBoardConfigState? managedConfig = store.State.ManagedBoardConfig;
        JsonObject ui = ParseManagedBoardObjectOrEmpty(managedConfig?.RawUiJson);
        JsonObject layoutBlob = ParseManagedBoardObjectOrEmpty(managedConfig?.RawLayoutJson);
        BoardCanvasLayoutState layoutState = store.GetCanvasLayout();

        async Task<JsonObject?> ShallowMergeAsync(string key, JsonNode? data)
        {
            string normalizedKey = key?.Trim() ?? string.Empty;
            if (normalizedKey.Length == 0)
            {
                return null;
            }

            JsonNode? saved = await client.ManageBoardsAsync("shallow-merge", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = targetBoardId,
                ["ns"] = winUiLayoutNamespace,
                ["key"] = normalizedKey,
                ["val"] = data?.DeepClone(),
            }).ConfigureAwait(false);

            return saved is JsonObject dataObject
                ? dataObject["layout"] as JsonObject
                : null;
        }

        UseEffect(
            () => () =>
            {
                autosaveTimerRef.Current?.Dispose();
                autosaveTimerRef.Current = null;
            },
            targetBoardId);

        async Task FlushLayoutAsync()
        {
            autosaveTimerRef.Current?.Dispose();
            autosaveTimerRef.Current = null;

            try
            {
                JsonNode canvasNode = layoutState.InfiniteCanvasBlob is { ValueKind: JsonValueKind.Object } blob
                    ? JsonNode.Parse(blob.GetRawText()) ?? new JsonObject()
                    : BuildLegacyCanvasNode(layoutState);

                _ = await ShallowMergeAsync("canvas", canvasNode).ConfigureAwait(false);
            }
            catch
            {
                // Layout save failures are non-fatal; the next autosave or explicit flush will retry.
            }
        }

        void ScheduleAutosave()
        {
            autosaveTimerRef.Current?.Dispose();
            autosaveTimerRef.Current = new Timer(
                _ => _ = FlushLayoutAsync(),
                null,
                TimeSpan.FromSeconds(30),
                Timeout.InfiniteTimeSpan);
        }

        BoardLayoutActions actions = new(
            SetCoords: (cardId, x, y) => store.SetCanvasCardPosition(cardId, x, y),
            SetManyCoords: byCardId =>
            {
                if (byCardId is null)
                {
                    return;
                }

                foreach (KeyValuePair<string, BoardCanvasPointState> entry in byCardId)
                {
                    if (entry.Value is null)
                    {
                        continue;
                    }

                    store.SetCanvasCardPosition(entry.Key, entry.Value.X, entry.Value.Y);
                }
            },
            SetWidth: (cardId, width) => store.SetCanvasCardWidth(cardId, width),
            SetViewport: (x, y, zoom) => store.SetCanvasViewport(x, y, zoom),
            SetInfiniteCanvasBlob: blob => store.SetInfiniteCanvasBlob(blob),
            FlushLayout: FlushLayoutAsync,
            ScheduleAutosave: ScheduleAutosave);

        return new BoardVisuals(
            new BoardVisualState(
                ui,
                layoutBlob,
                BoardTheme.ResolveThemePackIdFromLayoutJson(layoutBlob.ToJsonString()),
                ResolveCentrePaneKind(layoutBlob),
                layoutState),
            ShallowMergeAsync,
            actions);
    }

    /// <summary>Returns a single card's persisted width from the current canvas layout slice.</summary>
    protected (double? Width, Action<double> SetWidth) UseCardWidthState(string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        BoardCanvasLayoutState layout = store.GetCanvasLayout();
        double? width = !string.IsNullOrEmpty(cardId) && layout.Widths.TryGetValue(cardId, out double value)
            ? value
            : null;
        return (width, newWidth => store.SetCanvasCardWidth(cardId, newWidth));
    }

    internal static JsonObject ParseManagedBoardObjectOrEmpty(string? rawJson)
    {
        return ParseManagedBoardObjectOrNull(rawJson) ?? new JsonObject();
    }

    internal static JsonObject? ParseManagedBoardObjectOrNull(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(rawJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string ResolveCentrePaneKind(JsonObject layoutBlob)
    {
        return layoutBlob.TryGetPropertyValue("kind", out JsonNode? kindNode)
            && kindNode is JsonValue kindValue
            && kindValue.TryGetValue(out string? kindString)
            && !string.IsNullOrWhiteSpace(kindString)
                ? kindString.Trim()
                : DefaultCentrePaneKind;
    }

    private static JsonNode BuildLegacyCanvasNode(BoardCanvasLayoutState layoutState)
    {
        var cardIds = new JsonArray();
        foreach (string cardId in layoutState.CardIds)
        {
            cardIds.Add((JsonNode?)JsonValue.Create(cardId));
        }

        var positions = new JsonObject();
        foreach ((string cardId, BoardCanvasPointState position) in layoutState.Positions)
        {
            positions[cardId] = new JsonObject
            {
                ["x"] = JsonValue.Create(position.X),
                ["y"] = JsonValue.Create(position.Y),
            };
        }

        var widths = new JsonObject();
        foreach ((string cardId, double width) in layoutState.Widths)
        {
            widths[cardId] = JsonValue.Create(width);
        }

        JsonNode? viewport = layoutState.Viewport is null
            ? null
            : new JsonObject
            {
                ["x"] = JsonValue.Create(layoutState.Viewport.X),
                ["y"] = JsonValue.Create(layoutState.Viewport.Y),
                ["zoom"] = JsonValue.Create(layoutState.Viewport.Zoom),
            };

        return new JsonObject
        {
            ["cardIds"] = cardIds,
            ["positions"] = positions,
            ["widths"] = widths,
            ["viewport"] = viewport,
        };
    }
}