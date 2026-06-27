using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Left ingest rail (port of <c>registry/pane/GandalfPane.jsx</c>). A <see cref="PanelVertical"/> carousel
/// over the board's ingest cards: a header (eyebrow + card count), a prev/next nav over the matched cards,
/// and the active card rendered <c>bare</c>. Pane state (matched ids, carousel index, rail open) comes from
/// <see cref="HookComponent{TProps}.UsePaneState"/>; presence (hide-when-empty) is decided upstream by
/// <see cref="PaneRenderer"/>, so this component never gates itself.
/// </summary>
public sealed class GandalfPane : HookComponent<NodeProps>
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
            : Empty().Flex(grow: 1);

        return Component<PanelVertical, PanelVerticalProps>(new PanelVerticalProps(
            FabPosition: "top-left",
            Expanded: pane.Expanded,
            OnToggle: pane.ToggleExpanded,
            AriaLabel: "Ingest pane",
            Title: pane.Expanded ? "Hide ingest pane" : "Show ingest pane",
            Icon: "bi-chevron-right",
            IconToggled: "bi-chevron-left",
            Children: VStack(12,
                PaneRailUi.Header(theme, "Board Manager", pane.CardIds.Count),
                PaneRailUi.Nav(theme, pane.Cards, pane.Idx, pane.GoPrev, pane.GoNext),
                body)));
    }
}
