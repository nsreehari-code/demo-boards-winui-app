using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards.RuntimeHost;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>Props for <see cref="CardChrome"/> — a faithful port of <c>card/sub/CardChrome.jsx</c>'s prop set.</summary>
public sealed record CardChromeProps(
    string BoardId,
    string CardId,
    string Chrome = "full",
    bool EnableResize = false,
    Element? Children = null);

/// <summary>
/// Single owner of all card-tier chrome — a port of <c>CardChrome.jsx</c>. Every card kind renders its
/// body as <see cref="CardChromeProps.Children"/> inside this chrome so the header (title + status +
/// inspect / refresh / chat actions), the path-state overlay, the mini-chat + chat modal and the inspect
/// modal are defined once. Presentation is selected by <see cref="CardChromeProps.Chrome"/>:
/// <c>full</c> (board card with header), <c>bare</c> (no header; floating refresh) or <c>inspect</c>
/// (read-only preview frame used inside the inspect modal). DOM-only concerns (className/style, the
/// double-click focus <c>CustomEvent</c>, the pointer-drag resize listeners and bootstrap icon classes)
/// are dropped. The chat panes and inspect card are supplied through <see cref="CardChromeSeams"/> so
/// this tier stays independent of the (separately ported) connected chat / inspect clusters.
/// </summary>
public sealed class CardChrome : Component<CardChromeProps>
{
    internal const double BodyMaxHeight = 560;

    public override Element Render() =>
        Props.Chrome == "inspect"
            ? Component<CardChromeInspectView, CardChromeInspectViewProps>(
                new CardChromeInspectViewProps(Props.BoardId, Props.CardId, Props.Children))
            : Component<CardChromeBoardView, CardChromeBoardViewProps>(
                new CardChromeBoardViewProps(Props.BoardId, Props.CardId, Props.EnableResize, Props.Chrome, Props.Children));

    // ----- pure helpers (faithful ports of the module-level functions) -----

    internal const double MinCardWidth = 280;
    internal const double MaxCardWidth = 960;

    /// <summary>Lifecycle annotation of the exploration path the card belongs to (label / stamp / tone).</summary>
    internal readonly record struct PathStateDef(string Label, string Stamp, string ToneStatus);

    internal static readonly IReadOnlyDictionary<string, PathStateDef> PathStateDefs =
        new Dictionary<string, PathStateDef>(StringComparer.Ordinal)
        {
            ["suspended"] = new PathStateDef("Suspended", "Suspended", "blocked"),
            ["dead_ended"] = new PathStateDef("Dead-ended", "Ruled out", "failed"),
            ["wiped"] = new PathStateDef("Wiped", "Wiped", "secondary"),
        };

    internal static string NormalizePathState(IReadOnlyDictionary<string, string>? meta)
    {
        string raw = meta is not null && meta.TryGetValue("path_state", out string? value) && value is not null
            ? value.Trim().ToLowerInvariant()
            : string.Empty;
        return PathStateDefs.ContainsKey(raw) ? raw : string.Empty;
    }

    internal static string NormalizePathStateRationale(IReadOnlyDictionary<string, string>? meta) =>
        meta is not null && meta.TryGetValue("path_state_rationale", out string? value) && value is not null
            ? value.Trim()
            : string.Empty;

    internal static double ClampCardWidth(double nextWidth, double? viewportWidth = null)
    {
        double viewportMax = viewportWidth.HasValue
            ? Math.Max(MinCardWidth, Math.Min(MaxCardWidth, viewportWidth.Value - 48))
            : MaxCardWidth;
        return Math.Max(MinCardWidth, Math.Min(viewportMax, Math.Round(nextWidth)));
    }

    internal static string? MetaValue(BoardCard card, string key) =>
        card.MetaValues.TryGetValue(key, out string? value) ? value : null;

    internal static Element PathStateOverlay(string pathState, string rationale)
    {
        if (!PathStateDefs.TryGetValue(pathState, out PathStateDef def))
        {
            return Empty();
        }

        return TextBlock(def.Stamp)
            .FontSize(11)
            .Foreground(BoardTheme.CreateStatusBrush(def.ToneStatus, 0xFF));
    }
}

/// <summary>
/// Deferred seams for the connected chat panes and the inspect card. The card-tier chrome wires its
/// chat toggle / mini-chat / chat modal / inspect modal through these delegates; the (separately ported)
/// chat and inspect clusters assign them. Until then each surface renders empty, leaving the rest of the
/// chrome fully live.
/// </summary>
public static class CardChromeSeams
{
    /// <summary>Mini chat pane shown inline when chat is open. Args: boardId, cardId, onPopout.</summary>
    public static Func<string, string, Action, Element>? MiniChatPane;

    /// <summary>Full chat pane shown inside the chat modal. Args: boardId, cardId.</summary>
    public static Func<string, string, Element>? ChatPane;

    /// <summary>Inspect card modal. Args: boardId, cardId, title, onClose.</summary>
    public static Func<string, string, string, Action, Element>? InspectCard;
}

/// <summary>Props for the chat header toggle button (port of <c>ChatHeaderButton</c>).</summary>
public sealed record ChatHeaderButtonProps(string BoardId, string CardId, bool ChatOpen, Action OnToggleChat);

/// <summary>Header chat toggle — reads the card's agent-processing state for its title/label.</summary>
public sealed class ChatHeaderButton : HookComponent<ChatHeaderButtonProps>
{
    public override Element Render()
    {
        bool chatProcessing = UseChatStateAIWorking(Props.BoardId, Props.CardId);
        string label = Props.ChatOpen
            ? $"Close chat for {Props.CardId}"
            : chatProcessing ? $"Chat processing for {Props.CardId}" : $"Open chat for {Props.CardId}";
        return Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(Props.ChatOpen ? HostIconSources.ChatClose : HostIconSources.Chat, 15)), Props.OnToggleChat)
            .SubtleButton()
            .AutomationName(label);
    }
}

/// <summary>Props for <see cref="ResizableCardShell"/> (className is DOM-only and dropped).</summary>
public sealed record ResizableCardShellProps(string CardId, bool Enabled = false, Element? Children = null);

/// <summary>
/// Resizable shell — applies the card's persisted width from <c>useCardWidthState</c>. The live
/// pointer-drag handle (window pointer listeners + <c>document.body</c> cursor) is a DOM-only interaction
/// and is dropped; the bound width is still honoured.
/// </summary>
public sealed class ResizableCardShell : HookComponent<ResizableCardShellProps>
{
    public override Element Render()
    {
        (double? width, _) = UseCardWidthState(Props.CardId);
        Element body = Props.Children ?? Empty();
        return width is double w && w > 0 ? body.Width(w) : body;
    }
}

/// <summary>Props for the read-only inspect-preview chrome.</summary>
public sealed record CardChromeInspectViewProps(string BoardId, string CardId, Element? Children = null);

/// <summary>Read-only preview frame used inside the inspect modal (no header actions).</summary>
public sealed class CardChromeInspectView : HookComponent<CardChromeInspectViewProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        CardState? cardState = UseCardState(Props.BoardId, Props.CardId);

        if (cardState?.CardContent is not BoardCard content)
        {
            return Empty();
        }

        string title = CardChrome.MetaValue(content, "title") ?? Props.CardId;
        string status = cardState.CardRuntime?.Status ?? "fresh";
        string pathState = CardChrome.NormalizePathState(content.MetaValues);
        string rationale = CardChrome.NormalizePathStateRationale(content.MetaValues);

        Element header = VStack(2,
            TextBlock(title).Bold().FontSize(14).Foreground(theme.TextPrimary),
            status != "completed"
                ? TextBlock(status).FontSize(11).Foreground(BoardTheme.CreateStatusBrush(status, 0xFF))
                : Empty());

        Element body = ScrollViewer(VStack(8,
                CardChrome.PathStateOverlay(pathState, rationale),
                Props.Children ?? Empty()))
            .MaxHeight(CardChrome.BodyMaxHeight)
            .Set(scrollViewer =>
            {
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.VerticalScrollMode = ScrollMode.Auto;
            });

        Element card = SurfaceUi.CardSurface(theme, VStack(8, header, body));

        return Component<ResizableCardShell, ResizableCardShellProps>(
            new ResizableCardShellProps(Props.CardId, false, card));
    }
}

/// <summary>Props for the full / bare board-card chrome.</summary>
public sealed record CardChromeBoardViewProps(
    string BoardId,
    string CardId,
    bool EnableResize,
    string Chrome,
    Element? Children = null);

/// <summary>
/// Board-card chrome — the header (title + status + inspect / refresh / chat), the inline mini-chat,
/// the floating refresh for bare rails cards, the path-state overlay, plus the chat and inspect modals
/// (the latter two via <see cref="CardChromeSeams"/>).
/// </summary>
public sealed class CardChromeBoardView : HookComponent<CardChromeBoardViewProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        CardState? cardState = UseCardState(Props.BoardId, Props.CardId);
        (string? inspectedCardId, Action<string?> setInspectedCardId) = UseBoardInspectState();
        (bool chatOpen, Action<bool> setChatOpen) = UseState(false);
        (bool chatModalOpen, Action<bool> setChatModalOpen) = UseState(false);

        Action handleToggleChat = () => setChatOpen(!chatOpen);
        Action handleOpenChatModal = () =>
        {
            setChatOpen(false);
            setChatModalOpen(true);
        };

        if (cardState?.CardContent is not BoardCard content)
        {
            return Empty();
        }

        string title = CardChrome.MetaValue(content, "title") ?? Props.CardId;
        string status = cardState.CardRuntime?.Status ?? "fresh";
        string pathState = CardChrome.NormalizePathState(content.MetaValues);
        string rationale = CardChrome.NormalizePathStateRationale(content.MetaValues);
        bool inspectOpen = inspectedCardId == Props.CardId;
        bool refreshDisabled = cardState.CardRuntime?.Status == "running";
        bool showRefresh = cardState.CanRefresh;
        bool showHeader = Props.Chrome == "full";

        Element RefreshControl() => Button(
                Border(
                        refreshDisabled
                            ? ProgressRing()
                                .Width(14)
                                .Height(14)
                                .AutomationName("Refreshing")
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center)
                            : Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ArrowClockwise, 15))
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center))
                    .Width(15)
                    .Height(15),
                () => _ = cardState.CardActions.Refresh())
            .SubtleButton()
            .AutomationName(refreshDisabled ? "Refreshing" : "Refresh")
            .Set(button => button.IsEnabled = !refreshDisabled);

        Element header = Empty();
        Element headerStatus = Empty();
        if (showHeader)
        {
            Element titleBlock = TextBlock(title).Bold().FontSize(14).Foreground(theme.TextPrimary);

            headerStatus = status == "running"
                ? Border(
                        TextBlock(status)
                            .FontSize(11)
                            .Foreground(BoardTheme.CreateStatusBrush(status, 0xFF)))
                    .Padding(4)
                    .Background(theme.LayerAlt)
                    .CornerRadius(6)
                : Empty();

            Element inspectButton = Button(
                    Component<SvgIcon, SvgIconProps>(new SvgIconProps(inspectOpen ? HostIconSources.CardCloseDetails : HostIconSources.Sliders2, 15)),
                    () => setInspectedCardId(inspectOpen ? null : Props.CardId))
                .SubtleButton()
                .AutomationName(inspectOpen ? "Close inspect view" : "Show source information");

            Element actions = HStack(4,
                    inspectButton,
                    showRefresh ? RefreshControl() : Empty(),
                    Component<ChatHeaderButton, ChatHeaderButtonProps>(
                        new ChatHeaderButtonProps(Props.BoardId, Props.CardId, chatOpen, handleToggleChat)))
                .HorizontalAlignment(HorizontalAlignment.Right)
                .VAlign(VerticalAlignment.Center);

            header = Grid(
                new[] { GridSize.Star(), GridSize.Auto },
                new[] { GridSize.Auto },
                titleBlock.Grid(0, 0),
                actions.Grid(0, 1));
        }

        Element miniChat = chatOpen
            ? CardChromeSeams.MiniChatPane?.Invoke(Props.BoardId, Props.CardId, handleOpenChatModal) ?? Empty()
            : Empty();

        Element bodyActions = !showHeader && showRefresh ? RefreshControl() : Empty();

        Element body = ScrollViewer(VStack(8,
                miniChat,
                bodyActions,
                VStack(8,
                    CardChrome.PathStateOverlay(pathState, rationale),
                    Props.Children ?? Empty())))
            .MaxHeight(CardChrome.BodyMaxHeight)
            .Set(scrollViewer =>
            {
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.VerticalScrollMode = ScrollMode.Auto;
            });

        Element card = SurfaceUi.CardSurface(theme, VStack(8, header, headerStatus, body));

        Element shell = Component<ResizableCardShell, ResizableCardShellProps>(
            new ResizableCardShellProps(Props.CardId, Props.EnableResize, card));

        Element chatModal = chatModalOpen
            ? Component<GlobalModal, GlobalModalProps>(new GlobalModalProps(
                $"Chat: {title}",
                () => setChatModalOpen(false),
                CardChromeSeams.ChatPane?.Invoke(Props.BoardId, Props.CardId) ?? Empty()))
            : Empty();

        Element inspectModal = inspectOpen
            ? CardChromeSeams.InspectCard?.Invoke(Props.BoardId, Props.CardId, title, () => setInspectedCardId(null)) ?? Empty()
            : Empty();

        return VStack(0, shell, chatModal, inspectModal);
    }
}
