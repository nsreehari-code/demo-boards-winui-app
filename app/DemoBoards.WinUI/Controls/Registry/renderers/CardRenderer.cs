using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.Controls;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>Props for <see cref="CardRenderer"/> — the card to instantiate plus caller presentation context.</summary>
public sealed record CardRendererProps(
    string BoardId,
    string CardId,
    IReadOnlyList<RendererRule>? RendererRules = null,
    bool EnableResize = false,
    string Chrome = "full");

/// <summary>
/// Card tier resolution host — a faithful port of <c>renderers/CardRenderer.jsx</c>. A consumer of the
/// registry (not an entry): panes and the canvas call it to instantiate a card. It resolves the renderer
/// name via the config-driven <see cref="CardPresentationConfig.ResolveCardRenderer"/>, builds a node
/// <c>{ kind: 'card:&lt;renderer&gt;', spec }</c> and delegates to <see cref="NodeRenderer"/>. <c>chrome</c>
/// is presentation context supplied by the caller (full | inspect | bare) — it rides in <c>spec</c> like
/// <c>enableResize</c>, never via the registry/variant.
/// </summary>
public sealed class CardRenderer : HookComponent<CardRendererProps>
{
    public override Element Render()
    {
        CardState? cardState = UseCardState(Props.BoardId, Props.CardId);
        if (cardState?.CardContent is null)
        {
            return Empty();
        }

        string renderer = CardPresentationConfig.ResolveCardRenderer(cardState.CardContent, Props.RendererRules);

        var node = new RegistryNode(
            Kind: $"card:{renderer}",
            Spec: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boardId"] = Props.BoardId,
                ["cardId"] = Props.CardId,
                ["enableResize"] = Props.EnableResize,
                ["chrome"] = Props.Chrome,
            });

        return Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(Node: node));
    }
}
