using System.Collections.Generic;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// The outcome of resolving a <see cref="RegistryNode"/> against the registry and a namespace bag — the
/// pure half of the engine's resolution order (§5) factored out of the render shell so it can be exercised
/// without instantiating components. Mirrors the locked contract:
/// <c>visible → kind (resolveKind) → variant (resolveVariant) → data/currentValue</c>.
/// </summary>
public sealed record NodeResolution(
    bool Visible,
    string EffectiveKind,
    RegistryEntry? Entry,
    object? Data,
    string? Variant,
    object? CurrentValue,
    bool IsFallback);

/// <summary>
/// Pure resolution of a node — a faithful port of the non-rendering portion of <c>NodeRenderer.jsx</c>.
/// The render shell (<see cref="NodeRenderer"/>) consumes the result to instantiate the component and
/// apply framing; keeping this logic standalone keeps it directly testable.
/// </summary>
public static class NodeResolver
{
    private static readonly IReadOnlyDictionary<string, object?> EmptySpec
        = new Dictionary<string, object?>(System.StringComparer.Ordinal);

    public static NodeResolution Resolve(RegistryNode node, IReadOnlyDictionary<string, object?>? namespaces)
    {
        // 1. visibility gate: hidden only when a visible-bind is set AND resolves falsy.
        bool visible = string.IsNullOrEmpty(node.Visible)
            || JsTruthy(RegistryBind.ResolveBind(namespaces, node.Visible));

        IReadOnlyDictionary<string, object?> spec = node.Spec ?? EmptySpec;

        object? rawData = node.HasData
            ? node.Data
            : (node.Bind != null ? RegistryBind.ResolveBind(namespaces, node.Bind) : null);

        // 2. kind, with optional cross-entry redirect (resolveKind).
        RegistryEntry? requested = ComponentRegistry.LookupEntry(node.Kind);
        string effectiveKind = requested?.ResolveKind?.Invoke(spec, rawData) ?? node.Kind;
        RegistryEntry? entry = ComponentRegistry.ResolveEntry(effectiveKind);

        if (entry == null)
        {
            return new NodeResolution(visible, effectiveKind, null, rawData, null, null, false);
        }

        // Registry-level fallback coercion when data falls through to the text kind.
        bool isFallback = entry.Kind == ComponentRegistry.FallbackKind
            && effectiveKind != ComponentRegistry.FallbackKind;
        object? data = isFallback ? RegistryCoerce.CoerceUnknownData(rawData) : rawData;

        // 3. variant (within-entry submode; never swaps the component).
        string? variant = node.Variant ?? entry.ResolveVariant?.Invoke(spec, data) ?? entry.DefaultVariant;

        // 4. currentValue for controlled-commit inputs.
        object? currentValue = node.HasCurrentValue
            ? node.CurrentValue
            : (node.WriteTo != null ? RegistryBind.ResolveBind(namespaces, node.WriteTo) : null);

        return new NodeResolution(visible, effectiveKind, entry, data, variant, currentValue, isFallback);
    }

    /// <summary>
    /// JavaScript truthiness for resolved bind values: <c>null</c>, <c>false</c>, <c>0</c>, <c>""</c> and
    /// <c>NaN</c> are falsy; everything else — including empty arrays/objects — is truthy.
    /// </summary>
    public static bool JsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        double d => d != 0 && !double.IsNaN(d),
        float f => f != 0 && !float.IsNaN(f),
        int i => i != 0,
        long l => l != 0,
        _ => true,
    };
}
