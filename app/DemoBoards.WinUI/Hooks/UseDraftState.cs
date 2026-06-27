using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseDraftState — Reactor port of the frontend `useDraftState` hook
//  (demo-boards-frontend/src/hooks/useDraftState.js).
//
//  Layers a per-key edit journal on top of a *controlled* base dictionary:
//    • edits live in the journal (the base is never mutated),
//    • upstream changes to the base reconcile automatically — journal entries that now
//      match the new base are pruned, so an unrelated upstream update doesn't get
//      clobbered by a stale edit,
//    • a value is "committed" by lifting it into the base on the owning component, at
//      which point the matching journal entry prunes itself.
//
//  In Reactor preview.4 the hook primitives (UseReducer/UseEffect/UseMemo) are
//  *protected* members of Component and there is no public RenderContext.Current
//  accessor, so a reusable custom hook is exposed as a protected method on a thin
//  Component base class. Components that want the draft hook extend HookComponent<TProps>
//  instead of Component<TProps>; everything else about the component is unchanged.
// =====================================================================================

/// <summary>
/// Result of <see cref="HookComponent{TProps}.UseDraftState{TKey,TValue}"/>: a controlled-base draft
/// over a dictionary. <see cref="Values"/> is the base merged with the in-flight journal,
/// <see cref="Dirty"/> is true while edits are pending, <see cref="SetField"/> records (or clears)
/// one key, and <see cref="Discard"/> drops every pending edit.
/// </summary>
public readonly record struct DraftState<TKey, TValue>(
    IReadOnlyDictionary<TKey, TValue> Values,
    bool Dirty,
    Action<TKey, TValue> SetField,
    Action Discard)
    where TKey : notnull;

/// <summary>
/// A <see cref="Component{TProps}"/> base class that contributes reusable custom hooks. Extend this
/// instead of <see cref="Component{TProps}"/> to gain <see cref="UseDraftState{TKey,TValue}"/> and the
/// board/chat data hooks declared in the sibling <c>HookComponent.*.cs</c> partials.
/// </summary>
public abstract partial class HookComponent<TProps> : Component<TProps>
{
    /// <summary>
    /// Controlled-base draft over <paramref name="baseValues"/>. Edits are journaled per key; setting a
    /// key back to its base value clears the edit. Upstream changes to <paramref name="baseValues"/> are
    /// reconciled by pruning journal entries that now equal the base.
    /// </summary>
    protected DraftState<TKey, TValue> UseDraftState<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> baseValues,
        IEqualityComparer<TValue>? valueComparer = null)
        where TKey : notnull
    {
        IEqualityComparer<TValue> comparer = valueComparer ?? DraftDeepEqualityComparer<TValue>.Instance;

        // Per-key journal of in-flight edits. UseReducer's functional updater lets each setter compose
        // off the live previous journal rather than a render-time snapshot.
        var (journal, updateJournal) = UseReducer<IReadOnlyDictionary<TKey, TValue>>(
            new Dictionary<TKey, TValue>());

        // Reconcile against upstream: drop journal entries that now equal the controlled base, so an
        // external update to baseValues isn't clobbered by a stale edit.
        UseEffect(
            () =>
            {
                if (journal.Count > 0)
                {
                    Dictionary<TKey, TValue>? pruned = null;
                    foreach (KeyValuePair<TKey, TValue> entry in journal)
                    {
                        if (baseValues.TryGetValue(entry.Key, out TValue? baseValue)
                            && comparer.Equals(entry.Value, baseValue))
                        {
                            pruned ??= new Dictionary<TKey, TValue>(journal);
                            pruned.Remove(entry.Key);
                        }
                    }

                    if (pruned is not null)
                    {
                        updateJournal(_ => pruned);
                    }
                }

                return () => { };
            },
            baseValues,
            journal);

        // Merged view: base overlaid with the journal. Returns the base instance untouched when clean.
        IReadOnlyDictionary<TKey, TValue> values = UseMemo<IReadOnlyDictionary<TKey, TValue>>(
            () =>
            {
                if (journal.Count == 0)
                {
                    return baseValues;
                }

                var merged = new Dictionary<TKey, TValue>(baseValues.Count + journal.Count);
                foreach (KeyValuePair<TKey, TValue> entry in baseValues)
                {
                    merged[entry.Key] = entry.Value;
                }

                foreach (KeyValuePair<TKey, TValue> entry in journal)
                {
                    merged[entry.Key] = entry.Value;
                }

                return merged;
            },
            baseValues,
            journal);

        void SetField(TKey key, TValue value)
        {
            updateJournal(current =>
            {
                bool matchesBase = baseValues.TryGetValue(key, out TValue? baseValue)
                    && comparer.Equals(value, baseValue);

                if (matchesBase)
                {
                    if (!current.ContainsKey(key))
                    {
                        return current;
                    }

                    var trimmed = new Dictionary<TKey, TValue>(current);
                    trimmed.Remove(key);
                    return trimmed;
                }

                if (current.TryGetValue(key, out TValue? existing) && comparer.Equals(existing, value))
                {
                    return current;
                }

                return new Dictionary<TKey, TValue>(current)
                {
                    [key] = value,
                };
            });
        }

        void Discard()
        {
            updateJournal(current => current.Count == 0
                ? current
                : new Dictionary<TKey, TValue>());
        }

        return new DraftState<TKey, TValue>(values, journal.Count > 0, SetField, Discard);
    }
}

// Deep equality comparer used as the default for UseDraftState journal reconciliation.
// For value types and records that implement IEquatable<T>, defers to the built-in comparer
// (fast path). For other reference types, falls back to JSON-serialisation comparison,
// matching the frontend deepEqual default behavior.
file sealed class DraftDeepEqualityComparer<T> : IEqualityComparer<T>
{
    public static readonly DraftDeepEqualityComparer<T> Instance = new();

    public bool Equals(T? x, T? y)
    {
        if (EqualityComparer<T>.Default.Equals(x, y))
        {
            return true;
        }

        if (typeof(T).IsValueType)
        {
            return false;
        }

        if (x is null || y is null)
        {
            return false;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(x) ==
                   System.Text.Json.JsonSerializer.Serialize(y);
        }
        catch
        {
            return false;
        }
    }

    // GetHashCode intentionally returns 0 so Equals is always called; this is safe
    // because DraftDeepEqualityComparer is only used for change detection, never as
    // a dictionary or hash-set key comparer.
    public int GetHashCode(T obj) => 0;
}
