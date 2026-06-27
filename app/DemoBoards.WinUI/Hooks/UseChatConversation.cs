using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseChatConversation — Reactor port of useChatConversation.js
//
//  The single source of truth for a card's chat conversation orchestration:
//    • the chat SSE subscription lifecycle,
//    • live message accumulation (new turns append rather than replace),
//    • the immutable history/live boundary (HistoryAnchorTurnId),
//    • backward history pagination,
//    • draft turn-id rotation, and
//    • watch-party agent-output/tools channels (subscribed and live-updated via
//      UseReducedWatchParty → BoardStore.CardWatchParties → StateChanged re-render).
//  Consumers (the shared ChatPane) own only rendering; they receive the resolved
//  live/history message lists and the pagination/draft actions through props.
//
//  The web hook uses several useState slices updated with functional setters; Reactor's
//  UseState setter is value-only, so the mutable accumulation is modelled with a single
//  UseReducer record (functional dispatch) plus two non-rendering UseRefs. React effects
//  that key on the messages array are keyed here on a scalar message signature.
// =====================================================================================

/// <summary>Options for <see cref="HookComponent{TProps}.UseChatConversation"/> (mirrors the web hook's options bag).</summary>
public sealed record ChatConversationOptions(bool HistoryEnabled = true, int HistoryTurnsPerPage = 5);

/// <summary>The orchestrated conversation state returned by <see cref="HookComponent{TProps}.UseChatConversation"/>.</summary>
public sealed record ChatConversation(
    ChatTurns? Chat,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Messages,
    bool Processing,
    ChatActions? ChatActions,
    string? BoardSseClientId,
    string AgentOutput,
    string AgentTools,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> LiveMessages,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> HistoryMessages,
    string HistoryAnchorTurnId,
    bool HasMore,
    bool HistoryLoading,
    bool CanLoadMore,
    Action ShowPrevious,
    Func<Task> RefreshLatest,
    string DraftTurnId,
    Action RotateDraftTurn);

public abstract partial class HookComponent<TProps>
{
    private sealed record ChatConversationAccumulator(
        IReadOnlyList<LiveChatEntry> LiveMessages,
        string HistoryAnchorTurnId,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> History,
        bool HistoryLoading,
        bool HasMore,
        string CursorTurnId,
        string DraftTurnId);

    /// <summary>Port of <c>useChatConversation</c>: the full conversation orchestration for a card.</summary>
    protected ChatConversation UseChatConversation(string boardId, string cardId, ChatConversationOptions? options = null)
    {
        bool historyEnabled = options?.HistoryEnabled ?? true;
        int historyTurnsPerPage = options?.HistoryTurnsPerPage ?? 5;

        ChatTurns? chat = UseChatTurns(boardId, cardId);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> messages =
            chat?.Messages ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        bool processing = chat?.Processing ?? false;
        ChatActions? chatActions = chat?.ChatActions;
        string? boardSseClientId = chat?.BoardSseClientId;
        Func<string, int, Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>? onLoadPrevious =
            chat?.LoadPreviousTurns;
        EmbeddedBoardClient client = App.Current.BoardClient;
        string messagesSignature = ChatMessages.SignatureOf(messages);

        // Subscribe to chat SSE on mount so the runtime emits card_chats notifications.
        UseEffect(
            () =>
            {
                if (chatActions is null || string.IsNullOrEmpty(boardSseClientId) || string.IsNullOrEmpty(cardId))
                {
                    return () => { };
                }

                _ = chatActions.SubscribeChat();
                return () => { _ = chatActions.UnsubscribeChat(); };
            },
            boardId,
            cardId,
            boardSseClientId ?? string.Empty);

        var (state, dispatch) = UseReducer<ChatConversationAccumulator>(new ChatConversationAccumulator(
            LiveMessages: Array.Empty<LiveChatEntry>(),
            HistoryAnchorTurnId: string.Empty,
            History: Array.Empty<IReadOnlyDictionary<string, object?>>(),
            HistoryLoading: false,
            HasMore: true,
            CursorTurnId: string.Empty,
            DraftTurnId: ChatMessages.MakeTurnId()));

        var liveKeyRef = UseRef(string.Empty);
        var didInitialFetchRef = UseRef(false);

        async Task LoadBefore(string turnId)
        {
            if (string.IsNullOrEmpty(turnId) || onLoadPrevious is null)
            {
                return;
            }

            dispatch(prev => prev with { HistoryLoading = true });
            try
            {
                IReadOnlyList<IReadOnlyDictionary<string, object?>> older = await onLoadPrevious(turnId, historyTurnsPerPage);
                if (older.Count == 0)
                {
                    dispatch(prev => prev with { HasMore = false });
                    return;
                }

                dispatch(prev => prev with { History = ChatMessages.MergeMessageArrays(older, prev.History) });

                string nextCursorTurnId = ChatMessages.GetFirstTurnId(older);
                if (string.IsNullOrEmpty(nextCursorTurnId) || string.Equals(nextCursorTurnId, turnId, StringComparison.Ordinal))
                {
                    dispatch(prev => prev with { HasMore = false });
                    return;
                }

                bool hasMore = ChatMessages.CountDistinctTurns(older) >= historyTurnsPerPage;
                dispatch(prev => prev with { CursorTurnId = nextCursorTurnId, HasMore = hasMore });
            }
            catch
            {
                dispatch(prev => prev with { HasMore = false });
            }
            finally
            {
                dispatch(prev => prev with { HistoryLoading = false });
            }
        }

        // Reset all accumulation when the board/card changes so a reused instance does not bleed one
        // conversation into the next. Declared before the accumulation effect so it re-seeds cleanly.
        UseEffect(
            () =>
            {
                liveKeyRef.Current = string.Empty;
                didInitialFetchRef.Current = false;
                dispatch(prev => prev with
                {
                    LiveMessages = Array.Empty<LiveChatEntry>(),
                    History = Array.Empty<IReadOnlyDictionary<string, object?>>(),
                    HistoryAnchorTurnId = string.Empty,
                    HistoryLoading = false,
                    HasMore = true,
                    CursorTurnId = string.Empty,
                });
                return () => { };
            },
            boardId,
            cardId);

        // Accumulate live messages so new turns append rather than replace (the chat view may carry only
        // the latest turn).
        UseEffect(
            () =>
            {
                if (!historyEnabled)
                {
                    return () => { };
                }

                string key = $"{boardId}::{cardId}";
                dispatch(prev =>
                {
                    IReadOnlyList<LiveChatEntry> merged = liveKeyRef.Current != key
                        ? ChatMessages.MergeLiveMessages(Array.Empty<LiveChatEntry>(), messages)
                        : ChatMessages.MergeLiveMessages(prev.LiveMessages, messages);
                    liveKeyRef.Current = key;
                    return ReferenceEquals(merged, prev.LiveMessages) ? prev : prev with { LiveMessages = merged };
                });
                return () => { };
            },
            historyEnabled,
            boardId,
            cardId,
            messagesSignature);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> liveForDisplay = historyEnabled
            ? state.LiveMessages.Select(entry => entry.Msg).ToList()
            : messages;
        string firstLiveTurnId = ChatMessages.GetFirstTurnId(liveForDisplay);

        // Lock the history/live boundary once: the first turn id the live stream shows becomes the
        // immutable anchor. History is everything strictly before it.
        UseEffect(
            () =>
            {
                if (!historyEnabled || !string.IsNullOrEmpty(state.HistoryAnchorTurnId) || string.IsNullOrEmpty(firstLiveTurnId))
                {
                    return () => { };
                }

                dispatch(prev => prev.HistoryAnchorTurnId.Length > 0 ? prev : prev with { HistoryAnchorTurnId = firstLiveTurnId });
                return () => { };
            },
            historyEnabled,
            state.HistoryAnchorTurnId,
            firstLiveTurnId);

        // Exactly one automatic fetch, anchored at the immutable boundary turn id.
        UseEffect(
            () =>
            {
                if (!historyEnabled || didInitialFetchRef.Current || string.IsNullOrEmpty(state.HistoryAnchorTurnId))
                {
                    return () => { };
                }

                didInitialFetchRef.Current = true;
                string anchor = state.HistoryAnchorTurnId;
                dispatch(prev => prev with { CursorTurnId = anchor });
                _ = LoadBefore(anchor);
                return () => { };
            },
            historyEnabled,
            state.HistoryAnchorTurnId);

        // Rotate the draft turn id: adopt a pending file-upload turn, then mint a new one once that turn
        // has been consumed by a sent message.
        UseEffect(
            () =>
            {
                IReadOnlyDictionary<string, object?>? lastMsg = messages.Count > 0 ? messages[^1] : null;
                string lastTurnId = ChatMessages.GetMessageTurnId(lastMsg);

                if (IsPendingFileUploadMessage(lastMsg) && !string.IsNullOrEmpty(lastTurnId))
                {
                    dispatch(prev => string.Equals(prev.DraftTurnId, lastTurnId, StringComparison.Ordinal)
                        ? prev
                        : prev with { DraftTurnId = lastTurnId });
                    return () => { };
                }

                if (lastMsg is not null && !string.IsNullOrEmpty(lastTurnId))
                {
                    dispatch(prev => string.Equals(lastTurnId, prev.DraftTurnId, StringComparison.Ordinal)
                        ? prev with { DraftTurnId = ChatMessages.MakeTurnId() }
                        : prev);
                }

                return () => { };
            },
            messagesSignature,
            state.DraftTurnId);

        void ShowPrevious()
        {
            if (state.HistoryLoading)
            {
                return;
            }

            _ = LoadBefore(state.CursorTurnId);
        }

        async Task RefreshLatest()
        {
            IReadOnlyList<IReadOnlyDictionary<string, object?>> latest =
                await FetchChatMessagesBeforeTurnAsync(client, cardId, string.Empty, historyTurnsPerPage);
            if (latest.Count > 0)
            {
                dispatch(prev => prev with { History = ChatMessages.MergeMessageArrays(prev.History, latest) });
            }
        }

        void RotateDraftTurn() => dispatch(prev => prev with { DraftTurnId = ChatMessages.MakeTurnId() });

        return new ChatConversation(
            Chat: chat,
            Messages: messages,
            Processing: processing,
            ChatActions: chatActions,
            BoardSseClientId: boardSseClientId,
            AgentOutput: chat?.AgentOutput ?? string.Empty,
            AgentTools: chat?.AgentTools ?? string.Empty,
            LiveMessages: liveForDisplay,
            HistoryMessages: state.History,
            HistoryAnchorTurnId: state.HistoryAnchorTurnId,
            HasMore: state.HasMore,
            HistoryLoading: state.HistoryLoading,
            CanLoadMore: !string.IsNullOrEmpty(state.CursorTurnId),
            ShowPrevious: ShowPrevious,
            RefreshLatest: RefreshLatest,
            DraftTurnId: state.DraftTurnId,
            RotateDraftTurn: RotateDraftTurn);
    }

    private static bool IsPendingFileUploadMessage(IReadOnlyDictionary<string, object?>? msg)
    {
        if (msg is null || !string.Equals(BoardData.Str(msg, "role"), "system", StringComparison.Ordinal))
        {
            return false;
        }

        string text = (BoardData.Str(msg, "text") ?? string.Empty).Trim();
        return text.StartsWith("file uploaded:", StringComparison.OrdinalIgnoreCase);
    }
}
