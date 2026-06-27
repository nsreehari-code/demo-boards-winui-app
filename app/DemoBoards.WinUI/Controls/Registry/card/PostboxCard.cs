using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards.RuntimeHost;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Postbox card (port of <c>card/PostboxCard.jsx</c>) — backs both <c>card:postbox</c> and
/// <c>card:postbox-universal</c>. Renders a read-only feed of submitted evidence bundles
/// (<c>filegroups</c>, each a set of downloadable attachment chips plus an optional comment and
/// timestamp) above a <see cref="MessageWithAttachmentsInput"/> composer that uploads one or more staged
/// files as a new bundle. The structured <c>card_data</c> (<c>files</c>/<c>filegroups</c>) is read from
/// the resolved card definition exactly like <see cref="CardviewRenderer"/>; the upload is bound through
/// <see cref="CardActions.UploadCardFilesMultiple"/>.
/// <para>
/// DOM-only concerns are dropped: the className/style hooks, the bootstrap dropzone markup, the
/// stage-wide drag-and-drop overlay (the web wires dropped files into the composer through a ref the
/// Reactor composer does not expose — files are staged via the composer's own picker instead), the
/// smooth scroll-to-bottom effect, the composer-clear-on-card-switch effect (a fresh component mounts per
/// card here) and the transient "Uploading…" submit label.
/// </para>
/// </summary>
public sealed class PostboxCard : HookComponent<NodeProps>
{
    public override Element Render()
    {
        string boardId = BoardData.Str(Props.Spec, "boardId") ?? string.Empty;
        string cardId = BoardData.Str(Props.Spec, "cardId") ?? string.Empty;
        string chrome = BoardData.Str(Props.Spec, "chrome") ?? "full";
        bool enableResize = BoardData.BoolOr(Props.Spec, "enableResize", false);

        AppTheme theme = UseContext(AppThemeContext.Current);
        CardState? cardState = UseCardState(boardId, cardId);

        IReadOnlyDictionary<string, object?> cardData = ParseCardData(cardState?.CardContent);
        IReadOnlyList<object?> files = BoardData.ToList(BoardData.Get(cardData, "files")) ?? Array.Empty<object?>();
        IReadOnlyList<object?> filegroups = BoardData.ToList(BoardData.Get(cardData, "filegroups")) ?? Array.Empty<object?>();
        Func<IReadOnlyList<object?>, string?, Task>? uploadCardFilesMultiple = cardState?.CardActions.UploadCardFilesMultiple;

        if (cardState?.CardContent is null)
        {
            return Empty();
        }

        Element feed;
        if (filegroups.Count == 0)
        {
            feed = EmptyHint(theme);
        }
        else
        {
            var bubbles = new List<Element>(filegroups.Count);
            foreach (object? groupValue in filegroups)
            {
                bubbles.Add(BuildFilegroupBubble(theme, boardId, cardId, BoardData.AsMap(groupValue), files));
            }

            feed = ScrollViewer(VStack(12, bubbles.ToArray())).Flex(grow: 1);
        }

        Element composer = Component<MessageWithAttachmentsInput, MessageWithAttachmentsInputProps>(new MessageWithAttachmentsInputProps(
            Staged: true,
            Multiple: true,
            Disabled: uploadCardFilesMultiple is null,
            RequireAttachment: true,
            Placeholder: "Add comment (optional)",
            SubmitLabel: "Upload",
            OnSubmit: payload =>
            {
                if (uploadCardFilesMultiple is null)
                {
                    return;
                }

                IReadOnlyList<object?> staged = BoardData.ToList(BoardData.Get(payload, "files")) ?? Array.Empty<object?>();
                if (staged.Count == 0)
                {
                    return;
                }

                _ = uploadCardFilesMultiple(staged, (BoardData.Str(payload, "text") ?? string.Empty).Trim());
            }));

        return Component<CardChrome, CardChromeProps>(new CardChromeProps(
            boardId,
            cardId,
            chrome,
            enableResize,
            VStack(0, feed, composer).Flex(grow: 1)));
    }

    /// <summary>Reads the structured <c>card_data</c> slice (<c>files</c>/<c>filegroups</c>) from the card definition.</summary>
    internal static IReadOnlyDictionary<string, object?> ParseCardData(BoardCard? content)
    {
        if (content is null || string.IsNullOrWhiteSpace(content.RawDefinitionJson))
        {
            return BoardData.AsMap(null);
        }

        var card = RegistryJson.Parse(content.RawDefinitionJson) as IReadOnlyDictionary<string, object?>;
        return BoardData.AsMap(card != null && card.TryGetValue("card_data", out object? cd) ? cd : null);
    }

    /// <summary>Port of <c>formatTimestamp</c>: short local "MMM d, H:mm" (24h) or "" for empty/invalid input.</summary>
    internal static string FormatTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return string.Empty;
        }

        return parsed.ToLocalTime().ToString("MMM d, H:mm", CultureInfo.InvariantCulture);
    }

    private static Element EmptyHint(AppTheme theme) =>
        Border(TextBlock("Drag files anywhere in this pane or use the composer below to submit the first evidence bundle.")
                .FontSize(12)
                .Opacity(0.6)
                .Foreground(theme.TextPrimary)
                .HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Center))
            .Padding(16)
            .VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center)
            .Flex(grow: 1);

    // Port of FilegroupBubble: download chips for the group's files plus an optional comment + timestamp.
    private static Element BuildFilegroupBubble(
        AppTheme theme,
        string boardId,
        string cardId,
        IReadOnlyDictionary<string, object?> group,
        IReadOnlyList<object?> files)
    {
        string message = (BoardData.Str(group, "message") ?? string.Empty).Trim();
        string submittedAt = FormatTimestamp(BoardData.Str(group, "created_at"));
        IReadOnlyList<object?> fileIdxs = BoardData.ToList(BoardData.Get(group, "file_idxs")) ?? Array.Empty<object?>();

        var chips = new List<Element>();
        foreach (object? idxValue in fileIdxs)
        {
            if (!TryGetIndex(idxValue, out int fileIdx) || fileIdx < 0 || fileIdx >= files.Count)
            {
                continue;
            }

            IReadOnlyDictionary<string, object?> file = BoardData.AsMap(files[fileIdx]);
            if (string.IsNullOrEmpty(BoardData.Str(file, "stored_name")))
            {
                continue;
            }

            chips.Add(Component<PostboxDownloadChip, PostboxDownloadChipProps>(
                new PostboxDownloadChipProps(boardId, cardId, fileIdx, file)));
        }

        var bubbleChildren = new List<Element> { HStack(8, chips.ToArray()) };

        if (!string.IsNullOrEmpty(message) || !string.IsNullOrEmpty(submittedAt))
        {
            var meta = new List<Element>();
            if (!string.IsNullOrEmpty(message))
            {
                meta.Add(TextBlock(message).FontSize(12).Foreground(theme.TextPrimary));
            }

            if (!string.IsNullOrEmpty(submittedAt))
            {
                meta.Add(HStack(4,
                    Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.PostboxClock, 12)),
                    TextBlock(submittedAt).FontSize(11).Opacity(0.6).Foreground(theme.TextPrimary)));
            }

            bubbleChildren.Add(HStack(8, meta.ToArray()));
        }

        return Border(VStack(8, bubbleChildren.ToArray()))
            .Padding(12)
            .Background(theme.LayerAlt)
            .CornerRadius(12);
    }

    private static bool TryGetIndex(object? value, out int index)
    {
        switch (value)
        {
            case int i:
                index = i;
                return true;
            case long l:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                index = (int)d;
                return true;
            default:
                index = 0;
                return false;
        }
    }
}

/// <summary>Props for <see cref="PostboxDownloadChip"/>.</summary>
internal sealed record PostboxDownloadChipProps(
    string BoardId,
    string CardId,
    int Index,
    IReadOnlyDictionary<string, object?> File);

/// <summary>
/// Port of <c>DownloadFileChip</c>: a single attachment chip. Resolves the file's download URL through
/// <see cref="HookComponent{TProps}.UseCardFileUrl"/>; when present it renders a launchable paperclip
/// chip, otherwise a static label badge.
/// </summary>
internal sealed class PostboxDownloadChip : HookComponent<PostboxDownloadChipProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        string href = UseCardFileUrl(Props.BoardId, Props.CardId, Props.Index, Props.File);
        string label = CardViewShared.NonEmpty(BoardData.Str(Props.File, "name"))
            ?? CardViewShared.NonEmpty(BoardData.Str(Props.File, "stored_name"))
            ?? $"Attachment #{Props.Index}";

        if (string.IsNullOrEmpty(href))
        {
            return Border(TextBlock(label).FontSize(12).Foreground(theme.TextPrimary))
                .Padding(12, 6, 12, 6)
                .Background(theme.Layer)
                .CornerRadius(12);
        }

        Element content = HStack(4,
            Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.Paperclip, 14)),
            TextBlock(label).FontSize(12).Foreground(theme.TextPrimary));

        return Button(content, () =>
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(uri);
                }
            })
            .SubtleButton()
            .AutomationName(label);
    }
}
