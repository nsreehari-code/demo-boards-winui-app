using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Right truthset-explore rail (port of <c>registry/pane/TruthsetExplorePane.jsx</c>). The mirror of
/// <see cref="GandalfPane"/>: a right-anchored <see cref="PanelVertical"/> carousel over the board's
/// truthset cards, with a phase pill in the nav and a "No Truthset cards found." empty state when the
/// active card is absent. Pane state comes from <see cref="HookComponent{TProps}.UsePaneState"/>; presence
/// is decided upstream by <see cref="PaneRenderer"/>.
/// </summary>
public sealed class TruthsetExplorePane : HookComponent<NodeProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        IReadOnlyDictionary<string, object?> spec = Props.Spec;
        string boardId = spec.TryGetValue("boardId", out object? boardIdValue) && boardIdValue is string id ? id : string.Empty;
        IReadOnlyList<Func<BoardCardState, bool>>? includeFilters =
            spec.TryGetValue("includeFilters", out object? includeValue)
                ? includeValue as IReadOnlyList<Func<BoardCardState, bool>>
                : null;
        IReadOnlyList<RendererRule>? rendererRules =
            spec.TryGetValue("rendererRules", out object? rulesValue)
                ? rulesValue as IReadOnlyList<RendererRule>
                : null;

        PaneState pane = UsePaneState(boardId, includeFilters);

        Element body = pane.ActiveCardId != null
            ? Component<CardRenderer, CardRendererProps>(
                    new CardRendererProps(boardId, pane.ActiveCardId, rendererRules, Chrome: "bare"))
                .Flex(grow: 1)
            : EmptyState(theme);

        return Component<PanelVertical, PanelVerticalProps>(new PanelVerticalProps(
            FabPosition: "top-right",
            Expanded: pane.Expanded,
            OnToggle: pane.ToggleExpanded,
            AriaLabel: "Truthset Explore pane",
            Title: pane.Expanded ? "Hide Truthset Explore pane" : "Show Truthset Explore pane",
            Icon: "bi-chevron-left",
            IconToggled: "bi-chevron-right",
            Children: VStack(12,
                PaneRailUi.Header(theme, "Truthset Explore", pane.CardIds.Count),
                PaneRailUi.Nav(theme, pane.Cards, pane.Idx, pane.GoPrev, pane.GoNext, includePhasePill: true),
                body)));
    }

    /// <summary>Port of <c>TruthsetExploreEmptyState</c> — shown when no truthset card is active.</summary>
    private static Element EmptyState(AppTheme theme) =>
        Border(TextBlock("No Truthset cards found.")
                .FontSize(12)
                .Opacity(0.6)
                .Foreground(theme.TextPrimary)
                .HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Center))
            .Padding(16)
            .VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center)
            .Flex(grow: 1);
}
