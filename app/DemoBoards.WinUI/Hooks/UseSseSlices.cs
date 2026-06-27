// =====================================================================================
//  UseSseSlices — architectural divergence note (no implementation here)
//
//  In the frontend, useSseSlices.js is the backbone of the board-data layer. It owns
//  three distinct responsibilities:
//
//  1. Transport + accumulation
//     Opens a per-board EventSource (openBoardSse), fires a one-shot HTTP bootstrap
//     fetch (fetchBoardOneShotPayload), and applies every incoming SSE frame through
//     the platform-free board-sse-state reducer (applyBoardSseFrame). The resulting
//     reducer snapshot lives in a module-level boardStores Map.
//
//  2. Reactive slice selectors
//     Exports one useSyncExternalStore hook per data slice. React guarantees each
//     component re-renders when its slice changes and only then. Per-card result
//     objects are reference-stable (cached) so downstream useMemo deps stay correct.
//     Exported selectors and their callers:
//
//     Selector                         Direct callers
//     ──────────────────────────────   ──────────────────────────────────────────────
//     useBoardInfo                     useBoardState, useCardState, useChatState
//     useBoardStatus                   useBoardState
//     useBoardDataObjects              useBoardState, useCardState
//     useBoardCardDefinitionsAndData   useBoardState
//     useBoardCardRuntimes             useBoardState
//     useBoardCardIds                  useBoardState
//     useCardDefinitionAndData         useCardState
//     useCardRuntimeState              useCardState
//     useCardChatViews                 useChatState
//     useCardChatProcessing            useChatState
//     useCardChatWatchParty            useChatState
//
//  3. A separate UI-only store (boardUiStores)
//     Owns inspect/flip state that is not part of the runtime snapshot. Exported as
//     useBoardInspectState / useBoardFlipState and consumed by useBoardState.
//
// ─────────────────────────────────────────────────────────────────────────────────────
//  How WinUI handles each responsibility
// ─────────────────────────────────────────────────────────────────────────────────────
//
//  1. Transport + accumulation
//     DemoBoardsRuntimeService owns the embedded runtime. On every board change it
//     publishes BoardCanonicalStateChanged; BoardStore applies the canonical reducer
//     slices and raises BoardStore.StateChanged. This is the in-process equivalent of
//     the SSE stream + applyBoardSseFrame accumulation.
//
//  2. Reactive slice selectors → UseBoardStoreSubscription + named selector methods
//     in HookComponent.Store.cs (partial of HookComponent<TProps>):
//
//     Frontend selector                WinUI equivalent
//     ──────────────────────────────   ──────────────────────────────────────────────
//     useBoardInfo                     UseBoardInfo()
//     useBoardStatus                   UseBoardStatus()
//     useBoardDataObjects              UseBoardDataObjects()
//     useBoardCardDefinitionsAndData   UseBoardCardDefinitionsAndData()
//     useBoardCardRuntimes             UseBoardCardRuntimes()
//     useBoardCardIds                  UseBoardCardIds()
//     useCardDefinitionAndData         UseCardDefinitionAndData(cardId)
//     useCardRuntimeState              UseCardRuntimeState(cardId)
//     useCardChatViews                 UseCardChatView(cardId)
//     useCardChatProcessing            UseCardChatProcessing(cardId)
//     useCardChatWatchParty            UseCardWatchParty(cardId)
//
//     UseBoardStoreSubscription is the primitive behind all of these. It subscribes
//     to BoardStore.StateChanged (and optionally UiStateChanged) inside a UseEffect,
//     bumps a revision UseState on each notification, and returns the store. The
//     selector methods call UseBoardStoreSubscription once and then read from the store
//     directly — matching useSyncExternalStore's subscribe+getSnapshot contract.
//
//  3. UI-only store → BoardStore.UiState + UiStateChanged
//     useBoardInspectState / useBoardFlipState → UseBoardInspectState() in
//     HookComponent.Store.cs, which calls UseBoardStoreSubscription(includeUiState: true)
//     and reads/writes BoardStore.UiState.InspectedCardId.
//
// ─────────────────────────────────────────────────────────────────────────────────────
//  Structural divergence
// ─────────────────────────────────────────────────────────────────────────────────────
//
//  In the frontend the subscription guarantee is implicit: calling any useSseSlices
//  selector hook automatically wires the React component into the store. Forgetting
//  to call a selector simply means the component does not observe that slice — there
//  is no way to accidentally read stale data while appearing subscribed.
//
//  In WinUI the subscription must be explicit: a hook method that reads from
//  BoardStore must first call UseBoardStoreSubscription, otherwise it reads from an
//  unobserved store and the component will not re-render on change. The composite
//  hooks (UseBoardState, UseChatState, UseCardState, …) each call
//  UseBoardStoreSubscription exactly once at the top of the method, so all reads
//  within that call are reactive. Direct reads of App.Current.BoardStore outside a
//  hook context (e.g. in action callbacks) are intentionally non-reactive and are
//  correct — they always see the latest state at the moment of the call.
//
//  This file exists only as a named contract reference. All implementation lives in
//  HookComponent.Store.cs.
// =====================================================================================
