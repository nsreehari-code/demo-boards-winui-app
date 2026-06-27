using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatPaneBubblesList.jsx</c> — the scrollable conversation surface. It owns only view
/// concerns: which bubble is expanded and keeping the scroll pinned to the bottom as new live
/// messages arrive. All chat data is already orchestrated by the host and supplied through props; this
/// component just composes the presentational <see cref="ChatPaneHistory"/> and <see cref="ChatPaneLive"/>.
/// </summary>
public sealed record ChatPaneBubblesListProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? LiveMessages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? HistoryMessages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilesUploaded = null,
    Func<int, IReadOnlyDictionary<string, object?>, string?>? ResolveFileUrl = null,
    bool Compact = false,
    bool Processing = false,
    string AgentOutput = "",
    string AgentTools = "",
    bool HistoryEnabled = false,
    string HistoryAnchorTurnId = "",
    bool HasMore = false,
    bool HistoryLoading = false,
    bool CanLoadMore = false,
    Action? OnShowPrevious = null);

public sealed class ChatPaneBubblesList : Component<ChatPaneBubblesListProps>
{
    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);
        var (openMsgId, setOpenMsgId) = UseState<string?>(null);
        var scrollRef = UseRef<ScrollViewer?>(null);

        void ToggleExpand(string msgId) => setOpenMsgId(openMsgId == msgId ? null : msgId);

        int liveCount = Props.LiveMessages?.Count ?? 0;

        // Pin to the bottom whenever the live message count or the processing flag changes. The dep is a
        // single scalar (never a freshly-allocated tuple) so the effect only re-runs on real changes.
        int scrollKey = (liveCount << 1) | (Props.Processing ? 1 : 0);
        UseEffect(() =>
        {
            ScrollViewer? sv = scrollRef.Current;
            if (sv is null)
            {
                return;
            }

            sv.UpdateLayout();
            _ = sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: liveCount == 0 && !Props.Processing);
        }, scrollKey);

        var surfaces = new List<Element>();
        if (Props.HistoryEnabled && !string.IsNullOrEmpty(Props.HistoryAnchorTurnId))
        {
            surfaces.Add(Component<ChatPaneHistory, ChatPaneHistoryProps>(new ChatPaneHistoryProps(
                Messages: Props.HistoryMessages,
                FilesUploaded: Props.FilesUploaded,
                ResolveFileUrl: Props.ResolveFileUrl,
                Compact: Props.Compact,
                OpenMsgId: openMsgId,
                OnToggleExpand: ToggleExpand,
                HasMore: Props.HasMore,
                Loading: Props.HistoryLoading,
                CanLoadMore: Props.CanLoadMore,
                OnShowPrevious: Props.OnShowPrevious)));
        }

        surfaces.Add(Component<ChatPaneLive, ChatPaneLiveProps>(new ChatPaneLiveProps(
            Messages: Props.LiveMessages,
            FilesUploaded: Props.FilesUploaded,
            ResolveFileUrl: Props.ResolveFileUrl,
            Compact: Props.Compact,
            OpenMsgId: openMsgId,
            OnToggleExpand: ToggleExpand,
            Processing: Props.Processing,
            AgentOutput: Props.AgentOutput,
            AgentTools: Props.AgentTools)));

        return ScrollViewer(VStack(8, surfaces.ToArray()))
            .Set(scrollViewer =>
            {
                scrollRef.Current = scrollViewer;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            });
    }
}
