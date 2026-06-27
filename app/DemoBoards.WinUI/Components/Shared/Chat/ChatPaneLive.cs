using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatPaneLive.jsx</c> — the live conversation surface: the forward (live) message
/// bubbles plus the agent "working" bubble while a turn is in flight. Purely presentational; all
/// data arrives through props and expand state is owned by the parent.
/// </summary>
public sealed record ChatPaneLiveProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Messages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilesUploaded = null,
    Func<int, IReadOnlyDictionary<string, object?>, string?>? ResolveFileUrl = null,
    bool Compact = false,
    string? OpenMsgId = null,
    Action<string>? OnToggleExpand = null,
    bool Processing = false,
    string AgentOutput = "",
    string AgentTools = "");

public sealed class ChatPaneLive : Component<ChatPaneLiveProps>
{
    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);

        Element list = Component<MessageList, MessageListProps>(new MessageListProps(
            Messages: Props.Messages,
            FilesUploaded: Props.FilesUploaded,
            ResolveFileUrl: Props.ResolveFileUrl,
            Compact: Props.Compact,
            OpenMsgId: Props.OpenMsgId,
            OnToggleExpand: Props.OnToggleExpand,
            IdPrefix: "live"));

        if (!Props.Processing)
        {
            return list;
        }

        Element working = Component<AgentWorkingBubble, AgentWorkingBubbleProps>(new AgentWorkingBubbleProps(
            AgentOutput: Props.AgentOutput,
            AgentTools: Props.AgentTools,
            Compact: Props.Compact));

        return VStack(8, list, working);
    }
}
