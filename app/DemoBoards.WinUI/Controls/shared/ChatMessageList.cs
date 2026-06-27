using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChatMessageList.jsx</c> (<c>MessageList</c>) — renders a sequence of chat message
/// bubbles. Fully presentational: every message is a plain <c>{ role, text, files, turn }</c>
/// dictionary supplied through <c>Messages</c>, uploaded files arrive via <c>FilesUploaded</c>, and
/// file URLs are resolved through the <c>ResolveFileUrl</c> callback — the component reads no chat
/// data itself. Expand/collapse is lifted to the parent through <c>OpenMsgId</c> + <c>OnToggleExpand</c>
/// so only one bubble is expanded across the live and history surfaces.
/// </summary>
public sealed record MessageListProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Messages = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilesUploaded = null,
    Func<int, IReadOnlyDictionary<string, object?>, string?>? ResolveFileUrl = null,
    bool Compact = false,
    string? OpenMsgId = null,
    Action<string>? OnToggleExpand = null,
    string IdPrefix = "m");

public sealed class MessageList : Component<MessageListProps>
{
    private const int OverflowCharThreshold = 280;
    private const int OverflowLineThreshold = 7;

    private static readonly Regex IndexedSystemAttachment = new(
        @"^(file uploaded|AI generated):\s*(.*?)\s*#(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> messages = Props.Messages ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        if (messages.Count == 0)
        {
            return Empty();
        }

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var bubbles = new List<Element>(messages.Count);
        foreach (IReadOnlyDictionary<string, object?> msg in messages)
        {
            string role = BoardData.Str(msg, "role") ?? string.Empty;
            string turn = BoardData.Str(msg, "turn")?.Trim() is { Length: > 0 } t ? t : "noturn";
            string baseKey = $"{Props.IdPrefix}:{turn}:{role}";
            int occurrence = occurrences.TryGetValue(baseKey, out int count) ? count : 0;
            occurrences[baseKey] = occurrence + 1;
            string msgId = $"{baseKey}:{occurrence}";

            bubbles.Add(RenderBubble(theme, msg, role, msgId));
        }

        return VStack(8, bubbles.ToArray());
    }

    private Element RenderBubble(AppTheme theme, IReadOnlyDictionary<string, object?> msg, string role, string msgId)
    {
        if (role == "system")
        {
            return RenderSystemMessage(theme, msg);
        }

        bool isUser = role == "user";
        string text = BoardData.Str(msg, "text") ?? string.Empty;
        bool expanded = Props.OpenMsgId == msgId;
        bool overflowing = IsOverflowing(text);
        bool showFooter = overflowing || expanded;

        Element body = RenderMessageText(text, expanded);

        Element? attachments = RenderFileBadges(theme, msg);
        Element? footer = showFooter ? RenderExpandToggle(msgId, expanded) : null;

        return Component<ChatBubble, ChatBubbleProps>(new ChatBubbleProps(
            Variant: isUser ? "user" : "assistant",
            Attachments: attachments,
            Footer: footer,
            Children: body));
    }

    private static Element RenderMessageText(string text, bool expanded)
    {
        string normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return Empty();
        }

        Element markdown = Component<BoardMarkdown, BoardMarkdownProps>(new BoardMarkdownProps(normalized));
        if (expanded)
        {
            return markdown;
        }

        // Collapsed: clamp the body height via the fluent modifier (reapplied each render, unlike a
        // pooled-property '.Set' write which would be lost when the Border is reused).
        return Border(markdown).MaxHeight(112);
    }

    private Element? RenderFileBadges(AppTheme theme, IReadOnlyDictionary<string, object?> msg)
    {
        IReadOnlyList<object?>? files = BoardData.List(msg, "files");
        if (files is null || files.Count == 0)
        {
            return null;
        }

        Element[] badges = files
            .Select(file => (Element)Border(TextBlock(BoardValues.Stringify(file)).FontSize(11).Foreground(theme.TextPrimary))
                .Padding(4)
                .Background(theme.LayerAlt)
                .CornerRadius(6))
            .ToArray();
        return VStack(2, badges);
    }

    private Element RenderExpandToggle(string msgId, bool expanded)
    {
        string label = expanded ? "Collapse message" : "Expand message";
        return Button(
                Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ChatExpandChevron, 18, expanded ? 180 : 0)),
                () => Props.OnToggleExpand?.Invoke(msgId))
            .SubtleButton()
            .AutomationName(label)
            .HAlign(HorizontalAlignment.Center);
    }

    private Element RenderSystemMessage(AppTheme theme, IReadOnlyDictionary<string, object?> msg)
    {
        string text = BoardData.Str(msg, "text") ?? string.Empty;
        (string Kind, string Label, int Index)? indexed = ParseIndexedSystemAttachment(text);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> uploaded = Props.FilesUploaded ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        IReadOnlyDictionary<string, object?>? indexedFile = indexed is { } info && info.Index >= 0 && info.Index < uploaded.Count
            ? uploaded[info.Index]
            : null;

        Element? attachments = null;
        bool showText = true;
        if (indexed is { } attachment && indexedFile is not null)
        {
            string displayLabel = !string.IsNullOrEmpty(attachment.Label)
                ? attachment.Label
                : BoardData.Str(indexedFile, "name") ?? BoardData.Str(indexedFile, "stored_name") ?? $"Attachment #{attachment.Index}";
            attachments = RenderSystemAttachmentChip(theme, indexedFile, attachment.Index, displayLabel);
            showText = attachments is null;
        }

        Element? children = showText && text.Length > 0
            ? TextBlock(text).FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary)
                .Set(block => block.FontStyle = Windows.UI.Text.FontStyle.Italic)
            : null;

        return Component<ChatBubble, ChatBubbleProps>(new ChatBubbleProps(
            Variant: "system",
            Attachments: attachments,
            Children: children));
    }

    private Element? RenderSystemAttachmentChip(AppTheme theme, IReadOnlyDictionary<string, object?> file, int index, string label)
    {
        string? href = Props.ResolveFileUrl?.Invoke(index, file);
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        return Button($"\U0001F4CE {label}", () =>
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(uri);
                }
            })
            .SubtleButton()
            .AutomationName(label);
    }

    private static (string Kind, string Label, int Index)? ParseIndexedSystemAttachment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = IndexedSystemAttachment.Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
        {
            return null;
        }

        return (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value.Trim(), index);
    }

    private static bool IsOverflowing(string text)
    {
        string normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.Length > OverflowCharThreshold)
        {
            return true;
        }

        int lineCount = 1 + normalized.Count(ch => ch == '\n');
        return lineCount > OverflowLineThreshold;
    }
}
