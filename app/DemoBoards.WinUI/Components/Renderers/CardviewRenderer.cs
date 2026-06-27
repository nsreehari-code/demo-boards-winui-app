using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards.RuntimeHost;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>Props for <see cref="CardviewRenderer"/> — addresses the card whose view-tree to render.</summary>
public sealed record CardviewRendererProps(string BoardId, string CardId);

/// <summary>
/// Cardview tier resolution host — a faithful port of <c>renderers/CardviewRenderer.jsx</c>. A *consumer*
/// of the registry (not an entry): it reads card state, builds the binding namespaces
/// (<c>card</c>/<c>card_data</c>/<c>requires</c>/<c>computed_values</c>/<c>runtime_state</c>), resolves
/// the card's <c>view.elements</c> into leaf nodes (normalizeElement → buildLayoutNode → resolveRefKind)
/// and dispatches each through <see cref="NodeRenderer"/>. It also owns the data plumbing a view needs —
/// bind/writeTo patching, file-URL resolution and the optimistic save/overlay cycle.
/// DOM-only layout props (the Bootstrap <c>row/col</c> grid container classes/styles) are dropped in
/// favour of the engine's column stacking.
/// </summary>
public sealed class CardviewRenderer : HookComponent<CardviewRendererProps>
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyMap
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        EmbeddedBoardClient client = UseEmbeddedClient();

        CardState? cardState = UseCardState(Props.BoardId, Props.CardId);
        var (saving, setSaving) = UseState(false);
        Ref<string?> pendingUpstreamSignature = UseRef<string?>(null);

        // Null-safe signatures so every hook runs unconditionally (Reactor hook-order contract).
        BoardCard? content = cardState?.CardContent;
        string definitionJson = content?.RawDefinitionJson ?? string.Empty;
        string runtimeJson = cardState?.CardRuntime?.RawRuntimeJson ?? string.Empty;
        IReadOnlyDictionary<string, string> requires = cardState?.RequiresDataObjects
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        string requiresSignature = SignatureOf(requires);
        string upstreamSignature = string.Concat(
            cardState?.BoardSseClientId ?? string.Empty, "\u0001", definitionJson, "\u0001", runtimeJson);

        IReadOnlyDictionary<string, object?> namespaces = UseMemo(
            () => BuildNamespaces(Props.BoardId, definitionJson, runtimeJson, requires),
            Props.BoardId,
            definitionJson,
            runtimeJson,
            requiresSignature);

        LayoutResult layout = UseMemo(
            () => BuildLayout(namespaces, content),
            definitionJson,
            runtimeJson,
            requiresSignature);

        string cardId = Props.CardId;
        NodeServices services = UseMemo(
            () => new NodeServices(FileUrlForIndex: (index, file) =>
            {
                string storedName = BoardData.Str(file, "stored_name") ?? string.Empty;
                if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(storedName) || index < 0)
                {
                    return null;
                }

                string href = client.GetCardFileUrl(cardId, index, storedName) ?? string.Empty;
                return string.IsNullOrEmpty(href) ? null : href;
            }),
            cardId);

        // Clears the optimistic overlay once a fresh upstream payload lands (the save round-tripped).
        UseEffect(
            () =>
            {
                if (saving
                    && pendingUpstreamSignature.Current != null
                    && upstreamSignature != pendingUpstreamSignature.Current)
                {
                    pendingUpstreamSignature.Current = null;
                    setSaving(false);
                }

                return () => { };
            },
            saving,
            upstreamSignature);

        void BeginSaving()
        {
            pendingUpstreamSignature.Current = upstreamSignature;
            setSaving(true);
        }

        async Task HandleSaveAsync(object? value, IReadOnlyDictionary<string, object?> info)
        {
            if (cardState is null || saving)
            {
                return;
            }

            CardActions actions = cardState.CardActions;
            IReadOnlyDictionary<string, object?> cardData = namespaces.TryGetValue("card_data", out object? cd)
                ? cd as IReadOnlyDictionary<string, object?> ?? EmptyMap
                : EmptyMap;
            string? kind = info.TryGetValue("kind", out object? k) ? k as string : null;
            string? writeTo = info.TryGetValue("writeTo", out object? w) ? w as string : null;

            try
            {
                if (kind == "actions" && info.TryGetValue("buttonId", out object? buttonIdRaw) && buttonIdRaw is string buttonId)
                {
                    BeginSaving();
                    object? elemId = info.TryGetValue("elemId", out object? e) ? e : null;
                    await actions.DispatchAction("action", new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["buttonId"] = buttonId,
                        ["elemId"] = elemId,
                    });
                    return;
                }

                if (writeTo == "card_data" || (writeTo != null && writeTo.StartsWith("card_data.", StringComparison.Ordinal)))
                {
                    object? nextCardData = BuildNextCardData(cardData, writeTo!, value);
                    if (RegistryCoerce.DeepEqual(cardData, nextCardData))
                    {
                        return;
                    }

                    BeginSaving();
                    await actions.Patch(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["card_data"] = nextCardData,
                    });
                    return;
                }

                if (kind == "notes")
                {
                    object? currentNotes = cardData.TryGetValue("notes", out object? n) ? n : null;
                    if (RegistryCoerce.DeepEqual(currentNotes ?? string.Empty, value ?? string.Empty))
                    {
                        return;
                    }

                    var nextNotes = new Dictionary<string, object?>(cardData, StringComparer.Ordinal)
                    {
                        ["notes"] = value,
                    };
                    BeginSaving();
                    await actions.Patch(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["card_data"] = nextNotes,
                    });
                    return;
                }

                object? fieldValues = namespaces.TryGetValue("card", out object? cardRaw)
                    && cardRaw is IReadOnlyDictionary<string, object?> cardObj
                    && cardObj.TryGetValue("fieldValues", out object? fv)
                        ? fv
                        : null;
                if (RegistryCoerce.DeepEqual(fieldValues, value))
                {
                    return;
                }

                BeginSaving();
                await actions.Patch(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["fieldValues"] = value,
                });
            }
            catch
            {
                pendingUpstreamSignature.Current = null;
                setSaving(false);
                throw;
            }
        }

        void HandleSave(object? value, IReadOnlyDictionary<string, object?> info)
        {
            if (saving)
            {
                return;
            }

            _ = HandleSaveAsync(value, info);
        }

        // --- render output (all hooks have run) -------------------------------------------------
        if (content is null || layout.RawElementCount == 0)
        {
            return Empty();
        }

        var nodeElements = new List<Element>(layout.Nodes.Count);
        foreach (RegistryNode node in layout.Nodes)
        {
            nodeElements.Add(Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(
                Node: node,
                Namespaces: namespaces,
                Services: services,
                OnSave: HandleSave)));
        }

        Element body = VStack(8, nodeElements.ToArray());
        if (!saving)
        {
            return body;
        }

        Element indicator = HStack(8,
            ProgressRing(),
            TextBlock("Saving…").FontSize(12).Opacity(0.7).Foreground(theme.TextPrimary));

        return VStack(8, indicator, body.Opacity(0.5));
    }

    // ---- namespaces -----------------------------------------------------------------------------

    internal static IReadOnlyDictionary<string, object?> BuildNamespaces(
        string boardId,
        string definitionJson,
        string runtimeJson,
        IReadOnlyDictionary<string, string> requires)
    {
        var card = RegistryJson.Parse(definitionJson) as IReadOnlyDictionary<string, object?>;
        var runtime = RegistryJson.Parse(runtimeJson) as IReadOnlyDictionary<string, object?>;

        object? cardData = card != null && card.TryGetValue("card_data", out object? cd) ? cd : null;
        object? computedValues = runtime != null && runtime.TryGetValue("computed_values", out object? cv) ? cv : null;
        object? runtimeState = runtime != null && runtime.TryGetValue("runtime", out object? rs) ? rs : null;

        var requiresMap = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in requires)
        {
            requiresMap[entry.Key] = RegistryJson.ParseOrString(entry.Value);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["boardId"] = boardId,
            ["card"] = card ?? EmptyMap,
            ["card_data"] = cardData ?? EmptyMap,
            ["requires"] = requiresMap,
            ["computed_values"] = computedValues ?? EmptyMap,
            ["runtime_state"] = runtimeState ?? EmptyMap,
        };
    }

    // ---- view → layout nodes --------------------------------------------------------------------

    private sealed record LayoutResult(int RawElementCount, IReadOnlyList<RegistryNode> Nodes);

    private static LayoutResult BuildLayout(IReadOnlyDictionary<string, object?> namespaces, BoardCard? content)
    {
        if (content is null)
        {
            return new LayoutResult(0, Array.Empty<RegistryNode>());
        }

        var card = namespaces.TryGetValue("card", out object? cardRaw)
            ? cardRaw as IReadOnlyDictionary<string, object?>
            : null;
        IReadOnlyDictionary<string, object?>? view = card != null && card.TryGetValue("view", out object? v)
            ? v as IReadOnlyDictionary<string, object?>
            : null;
        IReadOnlyList<object?>? elements = view != null ? BoardData.List(view, "elements") : null;
        if (elements is null || elements.Count == 0)
        {
            return new LayoutResult(0, Array.Empty<RegistryNode>());
        }

        var nodes = new List<RegistryNode>(elements.Count);
        for (int index = 0; index < elements.Count; index++)
        {
            if (elements[index] is not IReadOnlyDictionary<string, object?> element)
            {
                continue;
            }

            // visibility filter (renderer-owned; the engine sees no `visible` on the built node)
            string? visible = element.TryGetValue("visible", out object? vis) ? vis as string : null;
            if (!string.IsNullOrEmpty(visible) && !NodeResolver.JsTruthy(RegistryBind.ResolveBind(namespaces, visible)))
            {
                continue;
            }

            nodes.Add(BuildLayoutNode(namespaces, element));
        }

        return new LayoutResult(elements.Count, nodes);
    }

    private static RegistryNode BuildLayoutNode(
        IReadOnlyDictionary<string, object?> namespaces,
        IReadOnlyDictionary<string, object?> element)
    {
        Normalized normalized = NormalizeElement(namespaces, element);
        return new RegistryNode(
            Kind: normalized.Kind,
            Id: element.TryGetValue("id", out object? id) ? id as string : null,
            Label: element.TryGetValue("label", out object? label) ? label as string : null,
            Spec: normalized.Spec,
            Bind: normalized.Bind,
            WriteTo: normalized.WriteTo,
            HasData: normalized.HasData,
            Data: normalized.Data);
    }

    internal readonly record struct Normalized(
        string Kind,
        IReadOnlyDictionary<string, object?> Spec,
        string? Bind,
        string? WriteTo,
        bool HasData,
        object? Data);

    // Authoring contract B: value lives in `data` ({ bind } | { value }), config in `spec`, the write
    // target in top-level `writeTo`. `ref` selects its effective kind/data/spec from a computed view
    // descriptor, falling back to the element's own data/spec.
    internal static Normalized NormalizeElement(
        IReadOnlyDictionary<string, object?> namespaces,
        IReadOnlyDictionary<string, object?> element)
    {
        string kind = (element.TryGetValue("kind", out object? k) ? k as string : null) ?? string.Empty;
        IReadOnlyDictionary<string, object?> elementSpec = element.TryGetValue("spec", out object? s)
            ? s as IReadOnlyDictionary<string, object?> ?? EmptyMap
            : EmptyMap;
        IReadOnlyDictionary<string, object?>? dataSource = element.TryGetValue("data", out object? d)
            ? d as IReadOnlyDictionary<string, object?>
            : null;
        string? writeTo = element.TryGetValue("writeTo", out object? w) ? w as string : null;

        if (kind != "ref")
        {
            (bool has, object? value) = ResolveElementValue(namespaces, dataSource);
            return new Normalized(
                Kind: kind,
                Spec: elementSpec,
                Bind: dataSource != null && dataSource.TryGetValue("bind", out object? b) ? b as string : null,
                WriteTo: writeTo,
                HasData: has,
                Data: value);
        }

        string? viewBind = elementSpec.TryGetValue("viewBind", out object? vb) ? vb as string : null;
        var refSpec = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> entry in elementSpec)
        {
            if (entry.Key is "viewBind" or "fallbackKind")
            {
                continue;
            }

            refSpec[entry.Key] = entry.Value;
        }

        object? viewRaw = !string.IsNullOrEmpty(viewBind) ? RegistryBind.ResolveBind(namespaces, viewBind) : null;
        IReadOnlyDictionary<string, object?> descriptor = viewRaw as IReadOnlyDictionary<string, object?> ?? EmptyMap;
        IReadOnlyDictionary<string, object?> descriptorSpec = descriptor.TryGetValue("spec", out object? ds)
            ? ds as IReadOnlyDictionary<string, object?> ?? EmptyMap
            : EmptyMap;
        IReadOnlyDictionary<string, object?>? descriptorData = descriptor.TryGetValue("data", out object? dd)
            ? dd as IReadOnlyDictionary<string, object?>
            : null;

        (bool refHas, object? effectiveData) = descriptorData != null
            ? ResolveElementValue(namespaces, descriptorData)
            : ResolveElementValue(namespaces, dataSource);
        string resolvedKind = ResolveRefKind(namespaces, elementSpec, effectiveData);

        var mergedSpec = new Dictionary<string, object?>(refSpec, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> entry in descriptorSpec)
        {
            mergedSpec[entry.Key] = entry.Value;
        }

        string? refBind = descriptorData != null && descriptorData.TryGetValue("bind", out object? db)
            ? db as string
            : dataSource != null && dataSource.TryGetValue("bind", out object? eb) ? eb as string : null;

        return new Normalized(
            Kind: resolvedKind,
            Spec: mergedSpec,
            Bind: refBind,
            WriteTo: writeTo,
            HasData: refHas,
            Data: effectiveData);
    }

    private static (bool Has, object? Value) ResolveElementValue(
        IReadOnlyDictionary<string, object?> namespaces,
        IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return (false, null);
        }

        if (source.TryGetValue("bind", out object? bind) && bind is string bindPath && !string.IsNullOrEmpty(bindPath))
        {
            return (true, RegistryBind.ResolveBind(namespaces, bindPath));
        }

        if (source.TryGetValue("value", out object? value))
        {
            return (true, value);
        }

        return (false, null);
    }

    private static string ResolveRefKind(
        IReadOnlyDictionary<string, object?> namespaces,
        IReadOnlyDictionary<string, object?> elementSpec,
        object? effectiveData)
    {
        string? viewBind = elementSpec.TryGetValue("viewBind", out object? vb) ? vb as string : null;
        object? viewRaw = !string.IsNullOrEmpty(viewBind) ? RegistryBind.ResolveBind(namespaces, viewBind) : null;
        if (viewRaw is string s && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (viewRaw is IReadOnlyDictionary<string, object?> descriptor
            && descriptor.TryGetValue("kind", out object? dk) && dk is string descriptorKind)
        {
            return descriptorKind;
        }

        if (elementSpec.TryGetValue("fallbackKind", out object? fk) && fk is string fallbackKind && !string.IsNullOrEmpty(fallbackKind))
        {
            return fallbackKind;
        }

        if (effectiveData is IReadOnlyList<object?>)
        {
            return "table";
        }

        if (effectiveData is string)
        {
            return "text";
        }

        return "narrative";
    }

    // ---- save helpers ---------------------------------------------------------------------------

    internal static object? BuildNextCardData(IReadOnlyDictionary<string, object?> cardData, string writeTo, object? value)
    {
        if (writeTo == "card_data")
        {
            if (value is IReadOnlyDictionary<string, object?> patch)
            {
                var next = new Dictionary<string, object?>(cardData, StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> entry in patch)
                {
                    next[entry.Key] = entry.Value;
                }

                return next;
            }

            return value;
        }

        string fieldPath = writeTo.Substring("card_data.".Length);
        return RegistryPath.DeepSet(cardData, fieldPath, value);
    }

    private static string SignatureOf(IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\u0001", map.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => string.Concat(entry.Key, "\u0002", entry.Value)));
    }
}
