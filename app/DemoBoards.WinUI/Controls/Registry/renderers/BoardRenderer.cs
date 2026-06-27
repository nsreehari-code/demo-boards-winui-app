using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Controls;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>Props for <see cref="BoardRenderer"/> — the board to resolve.</summary>
public sealed record BoardRendererProps(string BoardId);

/// <summary>
/// Board-tier resolution host (port of <c>renderers/BoardRenderer.jsx</c>). A <em>consumer</em> of the
/// registry, not an entry in it: it reads the managed board config (data) and resolves the board into a
/// node tree, delegating to <c>NodeRenderer</c>. Pane resolution is config-driven — filters, the centre
/// layout kind, and renderer rules. Panes are emitted as generic <c>pane</c> host nodes;
/// <see cref="PaneRenderer"/> decides presence (hides empty rails) and delegates to the concrete
/// <c>pane:&lt;kind&gt;</c>. The board entry is just a child-resolver over <c>spec.panes</c>.
/// <para>
/// The web's <c>holdCanvasUntilManagedConfig</c> only ever trips in the server-URL transport; the
/// embedded app always renders the centre surface, so that gate is constant-false here.
/// </para>
/// </summary>
public sealed class BoardRenderer : HookComponent<BoardRendererProps>
{
    /// <summary>Mirrors <c>useManagedBoardConfig</c>'s <c>DEFAULT_PANE_KIND</c>.</summary>
    private const string DefaultPaneKind = "infinite-canvas";

    public override Element Render()
    {
        // The whole registry (cardview / card / pane / board tiers) must be live before any node routes
        // through NodeRenderer.
        RegistryBootstrap.EnsureRegistered();

        string boardId = Props.BoardId;
        ManagedBoardConfigResult managed = UseManagedBoardConfig(boardId);
        JsonObject? uiConfig = managed.Config?.Ui;
        string? uiConfigJson = uiConfig?.ToJsonString();
        ManagedBoardLayout? boardLayout = managed.Config?.Layout;
        string centrePaneKind = boardLayout?.Kind ?? DefaultPaneKind;

        IReadOnlyList<Func<BoardCardState, bool>> ingestFilters =
            AdaptFilters(CardPresentationConfig.ResolvePaneFilters("gandalf", uiConfigJson));
        IReadOnlyList<Func<BoardCardState, bool>> truthsetFilters =
            AdaptFilters(CardPresentationConfig.ResolvePaneFilters("truthset", uiConfigJson));
        IReadOnlyList<Func<BoardCardState, bool>> centreExcludeFilters =
            ingestFilters.Concat(truthsetFilters).ToArray();
        IReadOnlyList<RendererRule> rendererRules = CardPresentationConfig.CompileRendererRules(uiConfigJson);

        IReadOnlyList<RegistryNode> panes = BuildPaneNodes(
            boardId, ingestFilters, truthsetFilters, centreExcludeFilters, centrePaneKind, rendererRules);

        var node = new RegistryNode(
            "board:default",
            Spec: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = boardId,
                ["panes"] = panes,
            });

        return Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(Node: node));
    }

    /// <summary>
    /// Builds the board's pane host nodes (gandalf rail, truthset rail, centre surface) exactly as the web
    /// <c>BoardRenderer</c> does. Pure so it can be exercised by the hooks harness.
    /// </summary>
    internal static IReadOnlyList<RegistryNode> BuildPaneNodes(
        string boardId,
        IReadOnlyList<Func<BoardCardState, bool>> ingestFilters,
        IReadOnlyList<Func<BoardCardState, bool>> truthsetFilters,
        IReadOnlyList<Func<BoardCardState, bool>> centreExcludeFilters,
        string centrePaneKind,
        IReadOnlyList<RendererRule> rendererRules)
    {
        return new[]
        {
            new RegistryNode("pane", Spec: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = boardId,
                ["paneKind"] = "gandalf",
                ["includeFilters"] = ingestFilters,
                ["layoutStrategy"] = "vertical",
                ["rendererRules"] = rendererRules,
            }),
            new RegistryNode("pane", Spec: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = boardId,
                ["paneKind"] = "truthset",
                ["includeFilters"] = truthsetFilters,
                ["layoutStrategy"] = "vertical",
                ["rendererRules"] = rendererRules,
            }),
            new RegistryNode("pane", Spec: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = boardId,
                ["paneKind"] = "centre",
                ["excludeFilters"] = centreExcludeFilters,
                ["layoutStrategy"] = centrePaneKind,
                ["rendererRules"] = rendererRules,
            }),
        };
    }

    // The web pane filters are predicates over the card object; the WinUI board hooks match over the
    // richer BoardCardState. Bridge each predicate so one filter source feeds both worlds.
    private static IReadOnlyList<Func<BoardCardState, bool>> AdaptFilters(IReadOnlyList<Func<BoardCard, bool>> cardFilters)
    {
        var adapted = new List<Func<BoardCardState, bool>>(cardFilters.Count);
        foreach (Func<BoardCard, bool> filter in cardFilters)
        {
            adapted.Add(state => state.CardContent is BoardCard card && filter(card));
        }

        return adapted;
    }
}
