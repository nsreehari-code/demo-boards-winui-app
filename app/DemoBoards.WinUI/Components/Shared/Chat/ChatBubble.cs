using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatBubble.jsx</c> — a chat message bubble. The <c>Variant</c> selects the appearance
/// (<c>user</c> right-aligned, <c>assistant</c> left-aligned, <c>system</c> centred notice). Content is
/// supplied via <c>Children</c> plus optional <c>Attachments</c> / <c>Footer</c> / <c>Icon</c> slots.
/// </summary>
public sealed record ChatBubbleProps(
    string Variant = "assistant",
    Element? Icon = null,
    bool? ShowIcon = null,
    Element? Attachments = null,
    Element? Footer = null,
    Element? Children = null);

public sealed class ChatBubble : Component<ChatBubbleProps>
{
    /// <summary>Default avatar glyph for <c>user</c> bubbles — mirrors <c>UserBubbleIcon</c>.</summary>
    public static Element UserBubbleIcon() => Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChatUserBubble, 14));

    /// <summary>Default avatar glyph for <c>assistant</c> bubbles — mirrors <c>AssistantBubbleIcon</c>.</summary>
    public static Element AssistantBubbleIcon() => Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChatAssistantBubble, 14));

    /// <summary>Wraps an avatar in the muted shell used beside the bubble — mirrors <c>ChatIconShell</c>.</summary>
    public static Element ChatIconShell(Element child) => Border(child).Opacity(0.55);

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        if (Props.Variant == "system")
        {
            var systemChildren = new List<Element>();
            if (Props.Children != null)
            {
                systemChildren.Add(Props.Children);
            }

            if (Props.Attachments != null)
            {
                systemChildren.Add(Props.Attachments);
            }

            return Border(VStack(4, systemChildren.ToArray()))
                .HAlign(HorizontalAlignment.Center);
        }

        bool isUser = Props.Variant == "user";
        bool renderIcon = Props.ShowIcon ?? true;
        Element? resolvedIcon = Props.Icon ?? Props.Variant switch
        {
            "user" => UserBubbleIcon(),
            "assistant" => AssistantBubbleIcon(),
            _ => null,
        };

        var bodyChildren = new List<Element>();
        if (Props.Children != null)
        {
            bodyChildren.Add(Props.Children);
        }

        if (Props.Attachments != null)
        {
            bodyChildren.Add(Props.Attachments);
        }

        var body = VStack(4, bodyChildren.ToArray());
        Element inner = renderIcon && resolvedIcon != null
            ? HStack(6, ChatIconShell(resolvedIcon), body.Flex(grow: 1))
            : body;

        var bubbleChildren = new List<Element> { inner };
        if (Props.Footer != null)
        {
            bubbleChildren.Add(Props.Footer);
        }

        return Border(VStack(4, bubbleChildren.ToArray()))
            .Padding(10)
            .Background(isUser ? theme.LayerAlt : theme.Layer)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12)
            .Set(border =>
            {
                border.MaxWidth = 420;
                border.HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            });
    }
}
