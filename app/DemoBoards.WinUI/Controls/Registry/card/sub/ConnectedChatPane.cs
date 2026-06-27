using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Connected chat panes — port of <c>shared/chat/ChatPane.jsx</c> (<c>ChatPane</c> / <c>GandalfChatPane</c> /
/// <c>MiniChatPane</c>). Each is the orchestration layer over <see cref="HookComponent{TProps}.UseChatConversation"/>:
/// it resolves the live/history messages, watch-party activity, draft turn id and send/upload actions, then
/// feeds the already-resolved data to the presentational <see cref="ChatPane"/>. The card's uploaded files
/// (for attachment chips) are read from the card definition exactly like the web's <c>useCardStateFilesData</c>,
/// and file URLs resolve through <see cref="EmbeddedBoardClient.GetCardFileUrl"/> like <c>useCardFileUrl</c>.
/// </summary>
public sealed record ConnectedChatPaneProps(
    string BoardId,
    string CardId,
    bool ReadOnly = false,
    bool Compact = false,
    string ComposerVariant = "default",
    bool HistoryEnabled = false,
    Action? OnPopout = null);

public sealed class ConnectedChatPane : HookComponent<ConnectedChatPaneProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        EmbeddedBoardClient client = App.Current.BoardClient;

        ChatConversation conv = UseChatConversation(
            Props.BoardId,
            Props.CardId,
            new ChatConversationOptions(HistoryEnabled: Props.HistoryEnabled));

        IReadOnlyList<IReadOnlyDictionary<string, object?>> filesUploaded =
            UseCardStateFilesData(Props.BoardId, Props.CardId);

        if (conv.Chat is null)
        {
            return Empty();
        }

        ChatActions? actions = conv.ChatActions;
        string draftTurnId = conv.DraftTurnId;
        bool processing = conv.Processing;

        Action<string>? onSubmit = actions is null
            ? null
            : text =>
            {
                string trimmed = text.Trim();
                if (trimmed.Length == 0)
                {
                    return;
                }

                FireAndForget(actions.SendChatAction(trimmed, new ChatSendOptions(TurnId: draftTurnId)));
            };

        Action<IReadOnlyList<IReadOnlyDictionary<string, object?>>>? onAttach = actions is null
            ? null
            : files =>
            {
                if (processing || files.Count == 0)
                {
                    return;
                }

                NativeAttachmentFile? file = ToNativeFile(files[0]);
                if (file is not null)
                {
                    FireAndForget(actions.UploadFileForChat(file, draftTurnId));
                }
            };

        Func<int, IReadOnlyDictionary<string, object?>, string?> resolveFileUrl =
            (index, file) => client.GetCardFileUrl(Props.CardId, index, BoardData.Str(file, "stored_name"));

        bool isMini = Props.ComposerVariant == "mini";
        Element? header = isMini && Props.OnPopout is not null
            ? ChatPane.PopoutHeader(Props.OnPopout, theme)
            : null;

        return Component<ChatPane, ChatPaneProps>(new ChatPaneProps(
            LiveMessages: conv.LiveMessages,
            HistoryMessages: conv.HistoryMessages,
            FilesUploaded: filesUploaded,
            ResolveFileUrl: resolveFileUrl,
            Processing: processing,
            AgentOutput: conv.AgentOutput,
            AgentTools: conv.AgentTools,
            HistoryEnabled: Props.HistoryEnabled,
            HistoryAnchorTurnId: conv.HistoryAnchorTurnId,
            HasMore: conv.HasMore,
            HistoryLoading: conv.HistoryLoading,
            CanLoadMore: conv.CanLoadMore,
            OnShowPrevious: conv.ShowPrevious,
            Compact: Props.Compact,
            ReadOnly: Props.ReadOnly,
            ComposerVariant: Props.ComposerVariant,
            Header: header,
            OnSubmit: onSubmit,
            OnAttach: onAttach));
    }

    private static NativeAttachmentFile? ToNativeFile(IReadOnlyDictionary<string, object?>? file)
    {
        if (file is null)
        {
            return null;
        }

        string name = BoardData.Str(file, "name") ?? string.Empty;
        string contentType = BoardData.Str(file, "contentType") ?? string.Empty;
        byte[] bytes = BoardData.Get(file, "bytes") is byte[] b ? b : Array.Empty<byte>();
        long size = (long)(BoardData.Dbl(file, "size") ?? bytes.Length);
        return new NativeAttachmentFile(name, contentType, bytes, size);
    }

    private static async void FireAndForget(System.Threading.Tasks.Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Parity with the web composer, which swallows send/upload rejections (.catch(() => {})).
        }
    }
}

/// <summary>
/// Static constructors for the connected chat panes (the named exports <c>ChatPane</c>/<c>GandalfChatPane</c>/
/// <c>MiniChatPane</c>), used to wire <see cref="CardChromeSeams"/> and the ingest card body.
/// </summary>
public static class ConnectedChat
{
    /// <summary>Full chat pane (port of <c>ChatPane</c>/<c>GandalfChatPane</c>): history enabled, default composer.</summary>
    public static Element Pane(string boardId, string cardId, bool compact = false) =>
        Component<ConnectedChatPane, ConnectedChatPaneProps>(new ConnectedChatPaneProps(
            BoardId: boardId,
            CardId: cardId,
            Compact: compact,
            HistoryEnabled: true));

    /// <summary>Mini chat pane (port of <c>MiniChatPane</c>): mini composer that hides while processing, with a pop-out header.</summary>
    public static Element Mini(string boardId, string cardId, Action? onPopout) =>
        Component<ConnectedChatPane, ConnectedChatPaneProps>(new ConnectedChatPaneProps(
            BoardId: boardId,
            CardId: cardId,
            ComposerVariant: "mini",
            HistoryEnabled: false,
            OnPopout: onPopout));
}
