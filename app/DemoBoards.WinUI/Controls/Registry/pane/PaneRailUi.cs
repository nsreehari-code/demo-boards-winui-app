using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Small presentational helpers shared by the carousel rails (<see cref="GandalfPane"/> /
/// <see cref="TruthsetExplorePane"/>) — a port of the repeated <c>board-ingest-pane__header</c> and
/// <c>board-icon-button</c> chrome in the frontend pane components. Kept here (rather than duplicated per
/// pane) because both rails share the exact same header row and prev/next button shape.
/// </summary>
internal static class PaneRailUi
{
    /// <summary>The eyebrow + card-count header (port of <c>board-ingest-pane__header</c>).</summary>
    internal static Element Header(AppTheme theme, string eyebrow, int count) =>
        HStack(8,
            TextBlock(eyebrow)
                .FontSize(11)
                .Bold()
                .Opacity(0.6)
                .Foreground(theme.TextPrimary)
                .Flex(grow: 1),
            TextBlock($"{count} cards")
                .FontSize(11)
                .Opacity(0.6)
                .Foreground(theme.TextPrimary));

    /// <summary>
    /// A circular prev/next nav button (port of <c>board-icon-button</c>) that renders one of the host
    /// chevron SVGs via the shared <see cref="SvgIcon"/> and disables itself at the carousel ends.
    /// </summary>
    internal static Element NavButton(string iconSource, string automationName, bool disabled, Action onClick) =>
        Button(
                Component<SvgIcon, SvgIconProps>(new SvgIconProps(iconSource, 16)),
                () =>
                {
                    if (!disabled)
                    {
                        onClick();
                    }
                })
            .SubtleButton()
            .AutomationName(automationName)
            .Width(32)
            .Height(32)
            .Set(button => button.IsEnabled = !disabled);

    /// <summary>
    /// The carousel nav row (port of <c>GandalfPaneNav</c> / <c>TruthsetExploreNav</c>): the active card's
    /// title, an optional phase pill (truthset only), prev/next chevron buttons and the <c>idx / total</c>
    /// counter. Prev disables at the first card, next at the last; an empty carousel disables both and
    /// shows a <c>—</c> counter — exactly the web's <c>disabled</c> logic.
    /// </summary>
    internal static Element Nav(
        AppTheme theme,
        IReadOnlyList<PaneCardSummary> cards,
        int idx,
        Action onPrev,
        Action onNext,
        bool includePhasePill = false)
    {
        int total = cards.Count;
        PaneCardSummary? card = idx >= 0 && idx < total ? cards[idx] : null;
        string title = card != null && card.Meta.TryGetValue("title", out string? metaTitle)
            ? metaTitle
            : card?.Id ?? "\u2014";

        bool prevDisabled = idx == 0 || total == 0;
        bool nextDisabled = total == 0 || idx >= total - 1;

        var row = new List<Element>
        {
            TextBlock(title)
                .Foreground(theme.TextPrimary)
                .Flex(grow: 1),
        };

        if (includePhasePill)
        {
            string phase = card != null && card.CardData.TryGetValue("phase", out string? phaseValue)
                ? phaseValue
                : "active";
            row.Add(PhasePill(theme, phase));
        }

        row.Add(NavButton(HostIconSources.NavChevronUp, "Previous card", prevDisabled, onPrev));
        row.Add(TextBlock(total > 0 ? $"{idx + 1} / {total}" : "\u2014")
            .FontSize(11)
            .Opacity(0.7)
            .Foreground(theme.TextPrimary));
        row.Add(NavButton(HostIconSources.NavChevronDown, "Next card", nextDisabled, onNext));

        return HStack(8, row.ToArray());
    }

    /// <summary>The truthset phase pill (port of <c>board-phase-pill</c>): accent tone for done, subtle otherwise.</summary>
    private static Element PhasePill(AppTheme theme, string phase) =>
        Border(TextBlock(phase)
                .FontSize(10)
                .Foreground(theme.TextPrimary))
            .Padding(6, 2, 6, 2)
            .CornerRadius(8)
            .Background(phase == "done" ? theme.Accent : theme.Layer);
}
