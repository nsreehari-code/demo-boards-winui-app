using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseCoordsState — Reactor port of useCoordsState.jsx
//
//  In the web app the canvas layout (card positions / widths / viewport) lives in a React
//  context populated by an autosaving provider, surfaced through useBoardLayoutState,
//  useBoardLayoutActions, useCoordsState and useCardWidthState. In the embedded app there
//  is a single board and BoardStore already owns the canvas layout (CanvasLayout slice +
//  SetCanvasCardPosition/Width/Viewport reducers), so the context collapses: the hooks read
//  from the store (subscribing for re-render) and bind the setters to it. FlushLayout persists
//  via EmbeddedBoardClient.SaveLayoutAsync (manage-boards save-layout), and ScheduleAutosave
//  debounces that to 30 s (matching the frontend DEFAULT_LAYOUT_AUTOSAVE_DELAY_MS).
// =====================================================================================

/// <summary>Stable layout action callbacks (port of <c>useBoardLayoutActions</c>).</summary>
public sealed record BoardLayoutActions(
    Action<string, double, double> SetCoords,
    Action<IReadOnlyDictionary<string, BoardCanvasPointState>> SetManyCoords,
    Action<string, double> SetWidth,
    Action<double, double, double> SetViewport,
    Func<Task> FlushLayout,
    Action ScheduleAutosave);

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useBoardLayoutState</c>: the current canvas layout slice.</summary>
    protected BoardCanvasLayoutState UseBoardLayoutState()
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return store.GetCanvasLayout();
    }

    /// <summary>Port of <c>useBoardLayoutActions</c>: setters bound to the in-process board store.</summary>
    protected BoardLayoutActions UseBoardLayoutActions()
    {
        BoardStore store = App.Current.BoardStore;
        EmbeddedBoardClient client = App.Current.BoardClient;
        var autosaveTimerRef = UseRef<Timer?>(null);

        // Dispose any pending autosave timer when the component unmounts.
        UseEffect(
            () => () =>
            {
                autosaveTimerRef.Current?.Dispose();
                autosaveTimerRef.Current = null;
            },
            store.GetBoardInfo().BoardId);

        async Task FlushLayoutAsync()
        {
            autosaveTimerRef.Current?.Dispose();
            autosaveTimerRef.Current = null;

            string boardId = store.GetBoardInfo().BoardId;
            BoardCanvasLayoutState layout = store.GetCanvasLayout();
            try
            {
                await client.SaveLayoutAsync(boardId, layout).ConfigureAwait(false);
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

        return new BoardLayoutActions(
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
            FlushLayout: FlushLayoutAsync,
            ScheduleAutosave: ScheduleAutosave);
    }

    /// <summary>Port of <c>useCoordsState(cardId)</c>: a single card's coords plus a bound setter.</summary>
    protected (BoardCanvasPointState? Coords, Action<double, double> SetCoords) UseCoordsState(string cardId)
    {
        BoardCanvasLayoutState layout = UseBoardLayoutState();
        BoardStore store = App.Current.BoardStore;
        BoardCanvasPointState? coords = !string.IsNullOrEmpty(cardId) && layout.Positions.TryGetValue(cardId, out BoardCanvasPointState? point)
            ? point
            : null;
        return (coords, (x, y) => store.SetCanvasCardPosition(cardId, x, y));
    }

    /// <summary>Port of <c>useCardWidthState(cardId)</c>: a single card's width plus a bound setter.</summary>
    protected (double? Width, Action<double> SetWidth) UseCardWidthState(string cardId)
    {
        BoardCanvasLayoutState layout = UseBoardLayoutState();
        BoardStore store = App.Current.BoardStore;
        double? width = !string.IsNullOrEmpty(cardId) && layout.Widths.TryGetValue(cardId, out double value)
            ? value
            : null;
        return (width, newWidth => store.SetCanvasCardWidth(cardId, newWidth));
    }
}
