using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatPaneHistory.jsx</c> — the history surface: a "Show previous messages" button plus
/// the already-fetched older bubbles. Purely presentational; all pagination state is owned by the
/// parent and reported back through <c>OnShowPrevious</c>.
/// </summary>
public sealed record ChatPaneHistoryProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Messages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilesUploaded = null,
    Func<int, IReadOnlyDictionary<string, object?>, string?>? ResolveFileUrl = null,
    bool Compact = false,
    string? OpenMsgId = null,
    Action<string>? OnToggleExpand = null,
    bool HasMore = false,
    bool Loading = false,
    bool CanLoadMore = false,
    Action? OnShowPrevious = null);

public sealed class ChatPaneHistory : Component<ChatPaneHistoryProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> messages = Props.Messages ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        if (messages.Count == 0 && !Props.HasMore)
        {
            return Empty();
        }

        var children = new List<Element>();
        if (Props.HasMore)
        {
            string label = Props.Loading ? "Loading previous messages\u2026" : "Show previous messages";
            children.Add(Button(label, () => Props.OnShowPrevious?.Invoke())
                .SubtleButton()
                .AutomationName(label)
                .HAlign(HorizontalAlignment.Center)
                .Set(button => button.IsEnabled = Props.CanLoadMore && !Props.Loading));
        }

        children.Add(Component<MessageList, MessageListProps>(new MessageListProps(
            Messages: messages,
            FilesUploaded: Props.FilesUploaded,
            ResolveFileUrl: Props.ResolveFileUrl,
            Compact: Props.Compact,
            OpenMsgId: Props.OpenMsgId,
            OnToggleExpand: Props.OnToggleExpand,
            IdPrefix: "history")));

        return VStack(8, children.ToArray());
    }
}
