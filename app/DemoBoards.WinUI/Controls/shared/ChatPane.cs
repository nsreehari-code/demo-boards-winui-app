using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatPane.jsx</c> (and its <c>GandalfChatPane</c>/<c>MiniChatPane</c> variants) — the chat
/// pane shell: an optional header, the scrollable <see cref="ChatPaneBubblesList"/>, and the
/// <see cref="ChatInput"/> composer. It is fully presentational: the host orchestrates the conversation
/// (in <c>useChatConversation</c> on the web) and feeds the already-resolved messages and live agent
/// activity through props, wiring <c>OnSubmit</c>/<c>OnAttach</c> to the real send/upload actions.
/// A <c>"mini"</c> <c>ComposerVariant</c> hides the composer while a turn is processing, matching
/// <c>MiniChatPane</c>; use <see cref="PopoutHeader"/> to build the mini header with a pop-out button.
/// </summary>
public sealed record ChatPaneProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? LiveMessages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? HistoryMessages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilesUploaded = null,
    Func<int, IReadOnlyDictionary<string, object?>, string?>? ResolveFileUrl = null,
    bool Processing = false,
    string AgentOutput = "",
    string AgentTools = "",
    bool HistoryEnabled = false,
    string HistoryAnchorTurnId = "",
    bool HasMore = false,
    bool HistoryLoading = false,
    bool CanLoadMore = false,
    Action? OnShowPrevious = null,
    bool Compact = false,
    bool ReadOnly = false,
    string ComposerVariant = "default",
    Element? Header = null,
    Action<string>? OnSubmit = null,
    Action<IReadOnlyList<IReadOnlyDictionary<string, object?>>>? OnAttach = null,
    string? Placeholder = null);

public sealed class ChatPane : Component<ChatPaneProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        var children = new List<Element>();
        if (Props.Header != null)
        {
            children.Add(Props.Header);
        }

        children.Add(Component<ChatPaneBubblesList, ChatPaneBubblesListProps>(new ChatPaneBubblesListProps(
            LiveMessages: Props.LiveMessages,
            HistoryMessages: Props.HistoryMessages,
            FilesUploaded: Props.FilesUploaded,
            ResolveFileUrl: Props.ResolveFileUrl,
            Compact: Props.Compact,
            Processing: Props.Processing,
            AgentOutput: Props.AgentOutput,
            AgentTools: Props.AgentTools,
            HistoryEnabled: Props.HistoryEnabled,
            HistoryAnchorTurnId: Props.HistoryAnchorTurnId,
            HasMore: Props.HasMore,
            HistoryLoading: Props.HistoryLoading,
            CanLoadMore: Props.CanLoadMore,
            OnShowPrevious: Props.OnShowPrevious)).Flex(grow: 1));

        bool isMini = Props.ComposerVariant == "mini";
        bool showComposer = !Props.ReadOnly && !(isMini && Props.Processing);
        if (showComposer)
        {
            children.Add(Component<ChatInput, ChatInputProps>(new ChatInputProps(
                OnSubmit: Props.OnSubmit,
                OnAttach: Props.OnAttach,
                Processing: Props.Processing,
                Placeholder: Props.Placeholder,
                Variant: Props.ComposerVariant)));
        }

        return Border(VStack(8, children.ToArray()))
            .Padding(8)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12);
    }

    /// <summary>
    /// Builds the mini-pane header (a "Chat" title plus a pop-out button) used by <c>MiniChatPane</c>.
    /// Pass the result as <see cref="ChatPaneProps.Header"/> with <c>ComposerVariant: "mini"</c>.
    /// </summary>
    public static Element PopoutHeader(Action onPopout, AppTheme theme) =>
        Border(HStack(6,
                TextBlock("Chat").FontSize(12).Bold().Foreground(theme.TextPrimary).Flex(grow: 1),
                Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChatPopout, 14)), onPopout)
                    .SubtleButton().AutomationName("Open full chat")))
            .Padding(4);
}
