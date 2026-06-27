using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Pane-tier resolution host (port of <c>renderers/PaneRenderer.jsx</c>). Dispatched by the engine as the
/// generic <c>pane</c> kind: it reads the pane's matching cards and <em>hides the rail when nothing
/// matches</em> (the centre surface is never gated — an empty board still shows its surface), otherwise
/// delegates to the concrete <c>pane:&lt;kind&gt;</c>. Presence is resolved here, never stored as state.
/// </summary>
public sealed class PaneRenderer : HookComponent<NodeProps>
{
    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> spec = Props.Spec;
        string boardId = spec.TryGetValue("boardId", out object? boardIdValue) && boardIdValue is string id ? id : string.Empty;
        string paneKind = spec.TryGetValue("paneKind", out object? paneKindValue) && paneKindValue is string kind ? kind : string.Empty;
        IReadOnlyList<Func<BoardCardState, bool>>? includeFilters =
            spec.TryGetValue("includeFilters", out object? includeValue)
                ? includeValue as IReadOnlyList<Func<BoardCardState, bool>>
                : null;

        // Hooks run unconditionally before any early return (the board slice is always read).
        BoardState board = UseBoardState(boardId);
        int matchedCount = board.FilterCards(includeFilters).Count;

        if (PaneIsHidden(paneKind, matchedCount))
        {
            return Empty();
        }

        var node = new RegistryNode($"pane:{paneKind}", Spec: spec);
        return Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(Node: node));
    }

    /// <summary>
    /// Presence rule (port of the web gate): gandalf/truthset rails only exist when cards match their
    /// filters; the centre surface always renders (it owns the empty-board state). Pure for the harness.
    /// </summary>
    internal static bool PaneIsHidden(string paneKind, int matchedCount) =>
        paneKind != "centre" && matchedCount == 0;
}
