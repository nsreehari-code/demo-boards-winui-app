using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Bounded recursion guard (conformance item 6) — a port of <c>recursion.js</c>. Each container render
/// descends one level; the engine refuses to render past <see cref="MaxRenderDepth"/>.
/// </summary>
public static class RenderDepthContext
{
    public const int MaxRenderDepth = 32;

    public static readonly Context<int> Current = new(0);
}

/// <summary>Props for <see cref="NodeRenderer"/> — the host injects the node plus the ambient bag.</summary>
public sealed record NodeRendererProps(
    RegistryNode? Node,
    IReadOnlyDictionary<string, object?>? Namespaces = null,
    object? Services = null,
    System.Action<object?, IReadOnlyDictionary<string, object?>>? OnSave = null,
    object? Status = null,
    Element? Children = null);

/// <summary>
/// The single resolver for every tier — a faithful port of <c>NodeRenderer.jsx</c>. It renders an entry's
/// <see cref="RegistryEntry.RenderComponentFn"/> directly (no per-kind adapter), following the locked
/// resolution order: visible → kind → variant → data/currentValue → children → framing. Pure resolution
/// lives in <see cref="NodeResolver"/>; this shell owns instantiation, recursion-depth context, container
/// child rendering and the engine's column framing.
/// </summary>
public sealed class NodeRenderer : Component<NodeRendererProps>
{
    public override Element Render()
    {
        int depth = UseContext(RenderDepthContext.Current);
        AppTheme theme = UseContext(AppThemeContext.Current);

        RegistryNode? node = Props.Node;
        if (node == null)
        {
            return Empty();
        }

        NodeResolution res = NodeResolver.Resolve(node, Props.Namespaces);

        // 1. visibility gate
        if (!res.Visible)
        {
            return Empty();
        }

        // entry not found (not even the fallback is registered)
        if (res.Entry == null)
        {
            return Frame(FallbackBox(theme, res.EffectiveKind, res.Data));
        }

        // 6. recursion guard
        if (depth >= RenderDepthContext.MaxRenderDepth)
        {
            return Frame(FallbackBox(theme, $"{res.EffectiveKind} (max depth {RenderDepthContext.MaxRenderDepth})", null));
        }

        RegistryEntry entry = res.Entry;
        IReadOnlyDictionary<string, object?> spec = node.Spec ?? EmptySpec;

        // 7. children (container extension)
        Element? children = RenderChildren(entry, spec, Props.Namespaces) ?? Props.Children;

        // instance-level metadata travels as one `meta` object so the prop contract never grows one-off props
        var meta = new NodeMeta(node.Label, node.Id);

        var componentProps = new NodeProps(
            Spec: spec,
            Meta: meta,
            Variant: res.Variant,
            Data: res.Data,
            CurrentValue: res.CurrentValue,
            WriteTo: node.WriteTo,
            OnSave: Props.OnSave,
            Status: Props.Status,
            Services: Props.Services,
            Children: children);

        // descend one recursion level for everything the component (and its children) renders
        Element rendered = entry.RenderComponentFn(componentProps)
            .Provide(RenderDepthContext.Current, depth + 1);

        // `meta.bare` opts a kind out of the engine's column framing so higher tiers that own their own
        // outer shell render directly with no injected wrapper.
        if (entry.Meta?.Bare == true)
        {
            return rendered;
        }

        // 8. own framing (meta.showLabel)
        bool showLabel = (entry.Meta?.ShowLabel != false) && !string.IsNullOrEmpty(node.Label);
        if (!showLabel)
        {
            return Frame(rendered);
        }

        Element label = TextBlock(node.Label!)
            .FontSize(12)
            .Bold()
            .Opacity(0.7)
            .Foreground(theme.TextPrimary);

        return VStack(8, label, rendered);
    }

    private Element? RenderChildren(
        RegistryEntry entry,
        IReadOnlyDictionary<string, object?> spec,
        IReadOnlyDictionary<string, object?>? namespaces)
    {
        if (entry.ChildResolver == null)
        {
            return null;
        }

        IReadOnlyList<RegistryNode> childNodes = entry.ChildResolver(spec, namespaces)
            ?? System.Array.Empty<RegistryNode>();

        var rendered = new List<Element>(childNodes.Count);
        foreach (RegistryNode child in childNodes)
        {
            rendered.Add(Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(
                Node: child,
                Namespaces: namespaces,
                Services: Props.Services,
                OnSave: Props.OnSave,
                Status: Props.Status)));
        }

        return VStack(0, rendered.ToArray());
    }

    private static Element Frame(Element child) => VStack(0, child);

    private static Element FallbackBox(AppTheme theme, string kind, object? data)
    {
        var parts = new List<Element>
        {
            TextBlock($"Unknown kind: {kind}").FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary),
        };

        if (data != null)
        {
            parts.Add(TextBlock(RegistryCoerce.CoerceUnknownData(data))
                .FontSize(12)
                .Opacity(0.65)
                .Foreground(theme.TextPrimary));
        }

        return Border(VStack(4, parts.ToArray()))
            .Padding(8)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(6);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptySpec
        = new Dictionary<string, object?>(System.StringComparer.Ordinal);
}
