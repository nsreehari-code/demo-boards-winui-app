using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  HookComponent — board store foundation (Reactor port of useSseSlices.js)
//
//  The web app exposes board/card state through useSseSlices.js: a per-board external
//  store fed by SSE, surfaced to React via useSyncExternalStore selector hooks. In the
//  embedded WinUI app there is no SSE — BoardStore already owns the snapshot in-process
//  and raises CLR events (StateChanged / UiStateChanged). The Reactor equivalent of
//  useSyncExternalStore is therefore: subscribe to those events inside a UseEffect and
//  bump a revision UseState to force a re-render, then read the latest values straight
//  off BoardStore. UseBoardStoreSubscription is that primitive; the selector hooks below
//  mirror the named useSseSlices selectors (useBoardInfo, useCardChatViews, ...).
//
//  Composite hooks (useChatState, useChatConversation, useCardState, ...) call
//  UseBoardStoreSubscription exactly once and then read via the static Read* helpers, so
//  a single component never registers redundant store subscriptions.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>
    /// Reactor equivalent of <c>useSyncExternalStore</c> for <see cref="BoardStore"/>: subscribes to the
    /// store's change events and forces a re-render when they fire. Call once per hosting hook, then read
    /// the latest data with the <c>Read*</c> helpers (or <see cref="App"/>.<c>Current.BoardStore</c>).
    /// </summary>
    protected BoardStore UseBoardStoreSubscription(bool includeUiState = true)
    {
        BoardStore store = App.Current.BoardStore;
        var (_, setRevision) = UseState(string.Empty);

        UseEffect(() =>
        {
            EventHandler<BoardStoreChangedEventArgs> onStateChanged = (_, _) => setRevision(Guid.NewGuid().ToString("N"));
            EventHandler<BoardUiState> onUiStateChanged = (_, _) => setRevision(Guid.NewGuid().ToString("N"));

            store.StateChanged += onStateChanged;
            if (includeUiState)
            {
                store.UiStateChanged += onUiStateChanged;
            }

            return () =>
            {
                store.StateChanged -= onStateChanged;
                if (includeUiState)
                {
                    store.UiStateChanged -= onUiStateChanged;
                }
            };
        });

        return store;
    }

    // ----- useSseSlices selector hooks ------------------------------------------------

    /// <summary>Port of <c>useBoardInfo</c>: the board id + embedded client id.</summary>
    protected BoardInfoState UseBoardInfo()
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return store.GetBoardInfo();
    }

    /// <summary>Port of <c>useBoardCardIds</c>: the sorted set of card ids on the board.</summary>
    protected IReadOnlyList<string> UseBoardCardIds()
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return store.GetBoardCardIds();
    }

    /// <summary>Port of <c>useCardDefinitionAndData</c>: a single card's definition + data, or null.</summary>
    protected BoardCard? UseCardDefinitionAndData(string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return string.IsNullOrEmpty(cardId) ? null : store.GetCardDefinitionAndData(cardId);
    }

    /// <summary>Port of <c>useCardRuntimeState</c>: a single card's runtime slice, or null.</summary>
    protected BoardCardRuntimeSlice? UseCardRuntimeState(string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return string.IsNullOrEmpty(cardId) ? null : store.GetCardRuntimeState(cardId);
    }

    /// <summary>Port of <c>useCardChatViews</c>: a single card's chat view slice, or null.</summary>
    protected BoardCardChatViewState? UseCardChatView(string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return ReadCardChatView(store, cardId);
    }

    /// <summary>Port of <c>useCardChatProcessing</c>: whether the card's agent is currently working.</summary>
    protected bool UseCardChatProcessing(string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return ReadCardChatView(store, cardId)?.Processing == true;
    }

    /// <summary>Port of <c>useCardChatWatchParty</c>: live agent output/tools for the card.</summary>
    protected BoardWatchpartyState UseCardWatchParty(string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        return string.IsNullOrEmpty(cardId)
            ? EmptyWatchparty
            : store.GetCardWatchparty(cardId);
    }

    /// <summary>Port of <c>useBoardInspectState</c> / <c>useBoardFlipState</c>: the inspected card id plus a setter.</summary>
    protected (string? InspectedCardId, Action<string?> SetInspectedCardId) UseBoardInspectState()
    {
        BoardStore store = UseBoardStoreSubscription();
        return (store.UiState.InspectedCardId, store.SetInspectedCardId);
    }

    // ----- static reads (used by composite hooks to avoid redundant subscriptions) -----

    internal static readonly BoardWatchpartyState EmptyWatchparty =
        new(string.Empty, string.Empty, Array.Empty<BoardWatchpartyToolPayload>());

    internal static BoardCardChatViewState? ReadCardChatView(BoardStore store, string cardId)
    {
        if (string.IsNullOrEmpty(cardId))
        {
            return null;
        }

        return store.State.CardChatViews.TryGetValue(cardId, out BoardCardChatViewState? view) ? view : null;
    }

    internal static BoardWatchpartyState ReadCardWatchParty(BoardStore store, string cardId) =>
        string.IsNullOrEmpty(cardId) ? EmptyWatchparty : store.GetCardWatchparty(cardId);

    /// <summary>Projects a runtime chat message into the plain prop-bag the shared chat components consume.</summary>
    internal static IReadOnlyDictionary<string, object?> ChatMessageToMap(BoardChatMessage message) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = message.Role ?? string.Empty,
            ["text"] = message.Text ?? string.Empty,
            ["turn"] = message.Turn ?? string.Empty,
            ["processing"] = message.Processing,
        };

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ChatMessagesToMaps(
        IReadOnlyList<BoardChatMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        var projected = new List<IReadOnlyDictionary<string, object?>>(messages.Count);
        foreach (BoardChatMessage message in messages)
        {
            projected.Add(ChatMessageToMap(message));
        }

        return projected;
    }
}
