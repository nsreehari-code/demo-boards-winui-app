using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseChatTurns — Reactor port of useChatTurns.js
//
//  Wraps UseChatState with a stable backward-pagination loader. LoadPreviousTurns
//  resolves the page of chat messages strictly before a given turn id, via the
//  inspect.chat-messages-on-cards MCP tool (port of fetchChatMessagesBeforeTurn).
// =====================================================================================

/// <summary>
/// The live chat state plus a backward-pagination action for older turns. Convenience members forward
/// to the wrapped <see cref="State"/> so consumers can read <c>Messages</c>/<c>Processing</c>/etc. directly,
/// mirroring the spread object the web hook returns.
/// </summary>
public sealed record ChatTurns(
    ChatState State,
    Func<string, int, Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> LoadPreviousTurns)
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Messages => State.Messages;

    public bool Processing => State.Processing;

    public bool Receiving => State.Receiving;

    public ChatActions ChatActions => State.ChatActions;

    public string? BoardSseClientId => State.BoardSseClientId;

    public string AgentOutput => State.AgentOutput;

    public string AgentTools => State.AgentTools;

    public ChatWatchParty WatchParty => State.WatchParty;
}

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useChatTurns</c>: chat state plus a stable <c>LoadPreviousTurns</c> pager, or null.</summary>
    protected ChatTurns? UseChatTurns(string boardId, string cardId)
    {
        ChatState? chat = UseChatState(boardId, cardId);
        EmbeddedBoardClient client = UseEmbeddedClient();

        Func<string, int, Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> loadPreviousTurns =
            UseMemo<Func<string, int, Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>>(
                () => (beforeTurnId, turns) => FetchChatMessagesBeforeTurnAsync(client, cardId, beforeTurnId, turns),
                cardId,
                chat?.BoardSseClientId ?? string.Empty,
                client.LiveBoardStateServerBaseUri.AbsoluteUri);

        return UseMemo<ChatTurns?>(
            () => chat is null ? null : new ChatTurns(chat, loadPreviousTurns),
            chat ?? (object)"\u2205",
            loadPreviousTurns);
    }

    /// <summary>
    /// Port of <c>fetchChatMessagesBeforeTurn</c>: a backward page of chat messages strictly before
    /// <paramref name="beforeTurnId"/> (or the newest page when it is empty), via the MCP inspect tool.
    /// </summary>
    internal static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchChatMessagesBeforeTurnAsync(
        EmbeddedBoardClient client,
        string cardId,
        string beforeTurnId,
        int turns)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["card_id"] = cardId,
            ["tail_turns"] = turns,
        };

        if (!string.IsNullOrWhiteSpace(beforeTurnId))
        {
            args["tail_turns_before_id"] = beforeTurnId;
        }

        string json = await client.CallBoardMcpAsync("inspect.chat-messages-on-cards", args).ConfigureAwait(false);
        return ChatMessages.ParseChatMessagesPayload(json);
    }
}
