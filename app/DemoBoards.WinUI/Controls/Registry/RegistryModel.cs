using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Instance-level metadata for a rendered node — mirrors the engine's <c>meta = { label, id }</c> object
/// (distinct from a registry entry's type-level <see cref="RegistryMeta"/> flags).
/// </summary>
public sealed record NodeMeta(string? Label = null, string? Id = null);

/// <summary>
/// The uniform prop contract every registered component receives — a faithful port of the props the
/// frontend <c>NodeRenderer</c> passes to <c>entry.renderComponentFn</c>:
/// <c>{ spec, meta, variant, data, currentValue, writeTo, onSave, status, services, children }</c>.
/// The contract never grows one-off props; instance metadata rides in <see cref="Meta"/>.
/// </summary>
public sealed record NodeProps(
    IReadOnlyDictionary<string, object?> Spec,
    NodeMeta Meta,
    string? Variant,
    object? Data,
    object? CurrentValue,
    string? WriteTo,
    Action<object?, IReadOnlyDictionary<string, object?>>? OnSave,
    object? Status,
    object? Services,
    Element? Children);

/// <summary>
/// A node the engine resolves and renders — mirrors the frontend node shape
/// <c>{ kind, id?, label?, variant?, spec?, bind?, writeTo?, visible?, data?, currentValue?, children? }</c>.
/// <see cref="HasData"/> / <see cref="HasCurrentValue"/> reproduce JavaScript's
/// <c>node.data !== undefined</c> distinction (a resolved <c>null</c> differs from "not provided").
/// </summary>
public sealed record RegistryNode(
    string Kind,
    string? Id = null,
    string? Label = null,
    string? Variant = null,
    IReadOnlyDictionary<string, object?>? Spec = null,
    string? Bind = null,
    string? WriteTo = null,
    string? Visible = null,
    bool HasData = false,
    object? Data = null,
    bool HasCurrentValue = false,
    object? CurrentValue = null,
    IReadOnlyList<RegistryNode>? Children = null);

/// <summary>
/// Type-level entry flags — mirrors a registry entry's <c>meta</c>: <c>showLabel</c> (engine frame label),
/// <c>isReadonly</c> (interaction model), <c>bare</c> (opt out of column framing), <c>controlled</c>
/// (commit model for inputs).
/// </summary>
public sealed record RegistryMeta(
    bool ShowLabel = true,
    bool IsReadonly = false,
    bool Bare = false,
    string? Controlled = null);

/// <summary>
/// A single registry entry — a faithful port of the entry shape documented in <c>registry.js</c>. The
/// component is stored as a factory (<see cref="RenderComponentFn"/>) so the engine can instantiate it
/// with the uniform <see cref="NodeProps"/> regardless of the concrete component type.
/// </summary>
public sealed record RegistryEntry(
    string Kind,
    Func<NodeProps, Element> RenderComponentFn,
    IReadOnlyList<string>? RequiredPropKeys = null,
    Func<IReadOnlyDictionary<string, object?>, object?, string?>? ResolveKind = null,
    string? DefaultVariant = null,
    Func<IReadOnlyDictionary<string, object?>, object?, string?>? ResolveVariant = null,
    RegistryMeta? Meta = null,
    Func<IReadOnlyDictionary<string, object?>, IReadOnlyDictionary<string, object?>?, IReadOnlyList<RegistryNode>>? ChildResolver = null);
