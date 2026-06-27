using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseChatState / UseChatActions — Reactor port of useChatState.js
//
//  Surfaces a card's chat conversation (messages + processing/receiving flags), the live
//  watch-party agent activity, and the stable action callbacks the composer wires to.
//  Data is read from BoardStore in-process; actions are bound to EmbeddedBoardClient.
//  Components stay presentational — they receive ChatState through props and call its
//  ChatActions; they never touch BoardStore/BoardClient directly.
// =====================================================================================

/// <summary>Optional payload for <see cref="ChatActions.SendChat"/> / <see cref="ChatActions.SendChatAction"/>.</summary>
public sealed record ChatSendOptions(
    string? TurnId = null,
    string? Role = null,
    IReadOnlyList<NativeAttachmentFile>? Files = null);

/// <summary>Stable chat action callbacks (port of <c>useChatActions</c>'s memoized object).</summary>
public sealed record ChatActions(
    Func<string, ChatSendOptions?, Task> SendChat,
    Func<string, ChatSendOptions?, Task> SendChatAction,
    Func<NativeAttachmentFile, string, Task> UploadFileForChat,
    Func<Task> SubscribeChat,
    Func<Task> UnsubscribeChat);

/// <summary>Reduced watch-party slice (port of <c>useReducedWatchParty</c>).</summary>
public sealed record ChatWatchParty(
    string AgentOutput,
    string AgentTools,
    IReadOnlyList<BoardWatchpartyToolPayload> AgentToolPayloads);

/// <summary>The full chat state object returned by <see cref="HookComponent{TProps}.UseChatState"/>.</summary>
public sealed record ChatState(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Messages,
    bool Processing,
    bool Receiving,
    string AgentOutput,
    string AgentTools,
    ChatWatchParty WatchParty,
    string? BoardSseClientId,
    ChatActions ChatActions);

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useChatActions</c>: memoized send/upload/subscribe callbacks for the card.</summary>
    protected ChatActions UseChatActions(string boardId, string cardId)
    {
        BoardInfoState boardInfo = UseBoardInfo();
        string? boardSseClientId = string.IsNullOrEmpty(boardInfo.ClientId) ? null : boardInfo.ClientId;
        EmbeddedBoardClient client = App.Current.BoardClient;

        return UseMemo<ChatActions>(
            () => new ChatActions(
                SendChat: (text, options) =>
                {
                    string turnId = (options?.TurnId ?? string.Empty).Trim();
                    string role = string.IsNullOrWhiteSpace(options?.Role) ? "user" : options!.Role!.Trim();
                    IReadOnlyList<NativeAttachmentFile> files = options?.Files ?? Array.Empty<NativeAttachmentFile>();
                    _ = role; // role is forwarded by the runtime entry; retained for parity with the web contract.
                    return client.AddChatEntryAndAnyAttachmentsAsync(cardId, text, turnId, files);
                },
                SendChatAction: (text, options) =>
                {
                    string turnId = (options?.TurnId ?? string.Empty).Trim();
                    var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["text"] = text };
                    if (!string.IsNullOrEmpty(turnId))
                    {
                        payload["turn-id"] = turnId;
                    }

                    return client.DispatchActionAsync(cardId, "chat-send", payload);
                },
                UploadFileForChat: (file, turnId) =>
                    client.AddChatAttachmentAsync(cardId, turnId ?? string.Empty, file),
                SubscribeChat: () =>
                    boardSseClientId is null ? Task.CompletedTask : client.SubscribeCardChatsAsync(cardId),
                UnsubscribeChat: () =>
                    boardSseClientId is null ? Task.CompletedTask : client.UnsubscribeCardChatsAsync(cardId)),
            cardId,
            boardSseClientId ?? string.Empty);
    }

    /// <summary>Port of <c>useReducedWatchParty</c>: subscribes the agent-output/tools channels and reduces the slice.</summary>
    protected ChatWatchParty UseReducedWatchParty(string boardId, string cardId, string? boardSseClientId)
    {
        BoardWatchpartyState watchParty = UseCardWatchParty(cardId);
        EmbeddedBoardClient client = App.Current.BoardClient;
        WinUiBoardServerConstants channels = App.Current.HostConfig.Frontend.BoardServerConstants;

        UseEffect(
            () =>
            {
                if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(boardSseClientId))
                {
                    return () => { };
                }

                _ = client.SubscribeWatchpartyAsync(cardId, channels.AgentOutputChannel);
                _ = client.SubscribeWatchpartyAsync(cardId, channels.AgentToolsChannel);

                return () =>
                {
                    _ = client.UnsubscribeWatchpartyAsync(cardId, channels.AgentToolsChannel);
                    _ = client.UnsubscribeWatchpartyAsync(cardId, channels.AgentOutputChannel);
                };
            },
            cardId,
            boardSseClientId ?? string.Empty);

        return UseMemo<ChatWatchParty>(
            () => new ChatWatchParty(
                watchParty.AgentOutput ?? string.Empty,
                watchParty.AgentTools ?? string.Empty,
                watchParty.AgentToolPayloads ?? Array.Empty<BoardWatchpartyToolPayload>()),
            watchParty.AgentOutput ?? string.Empty,
            watchParty.AgentTools ?? string.Empty,
            watchParty.AgentToolPayloads?.Count ?? 0);
    }

    /// <summary>Port of <c>useChatState</c>: the composed chat state for a card, or null when no card is selected.</summary>
    protected ChatState? UseChatState(string boardId, string cardId)
    {
        BoardCardChatViewState? chat = UseCardChatView(cardId);
        BoardInfoState boardInfo = UseBoardInfo();
        ChatActions chatActions = UseChatActions(boardId, cardId);
        string? boardSseClientId = string.IsNullOrEmpty(boardInfo.ClientId) ? null : boardInfo.ClientId;
        ChatWatchParty watchParty = UseReducedWatchParty(boardId, cardId, boardSseClientId);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> messages = ChatMessagesToMaps(chat?.Messages);
        bool processing = chat?.Processing == true;
        bool receiving = chat?.Receiving == true;

        return UseMemo<ChatState?>(
            () => string.IsNullOrEmpty(cardId)
                ? null
                : new ChatState(
                    Messages: messages,
                    Processing: processing,
                    Receiving: receiving,
                    AgentOutput: watchParty.AgentOutput,
                    AgentTools: watchParty.AgentTools,
                    WatchParty: watchParty,
                    BoardSseClientId: boardSseClientId,
                    ChatActions: chatActions),
            cardId,
            ChatMessages.SignatureOf(messages),
            processing,
            receiving,
            watchParty.AgentOutput,
            watchParty.AgentTools,
            boardSseClientId ?? string.Empty);
    }

    /// <summary>Port of <c>useChatStateAIWorking</c>: whether the card's agent is currently processing.</summary>
    protected bool UseChatStateAIWorking(string boardId, string cardId) => UseCardChatProcessing(cardId);

    /// <summary>Port of <c>useChatWatchParty</c>: the reduced watch-party slice for the card.</summary>
    protected ChatWatchParty UseChatWatchParty(string boardId, string cardId)
    {
        BoardInfoState boardInfo = UseBoardInfo();
        string? boardSseClientId = string.IsNullOrEmpty(boardInfo.ClientId) ? null : boardInfo.ClientId;
        return UseReducedWatchParty(boardId, cardId, boardSseClientId);
    }
}
