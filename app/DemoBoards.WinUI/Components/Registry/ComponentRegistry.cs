using System;
using System.Collections.Generic;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// The single unified component registry, keyed by <c>kind</c> and shared across all tiers
/// (board / pane / card / cardview). A faithful port of <c>registry.js</c>: one registry, no per-tier
/// namespaces, so any kind is addressable from any slot. Entries with later registration win on key
/// collision (mirrors <c>Map.set</c> overwrite semantics).
/// </summary>
public static class ComponentRegistry
{
    public const string FallbackKind = "text";

    private static readonly Dictionary<string, RegistryEntry> Registry = new(StringComparer.Ordinal);

    /// <summary>Registers entries, skipping null/blank-kind entries; later entries overwrite earlier ones.</summary>
    public static void RegisterEntries(IEnumerable<RegistryEntry>? entries)
    {
        if (entries == null)
        {
            return;
        }

        foreach (RegistryEntry entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Kind))
            {
                continue;
            }

            Registry[entry.Kind] = entry;
        }
    }

    /// <summary>Exact-match lookup — returns <c>null</c> when the kind is not registered.</summary>
    public static RegistryEntry? LookupEntry(string? kind)
        => kind != null && Registry.TryGetValue(kind, out RegistryEntry? entry) ? entry : null;

    /// <summary>
    /// Resolves the entry for <paramref name="kind"/>, falling back to <see cref="FallbackKind"/> when
    /// unknown. Returns <c>null</c> only when even the fallback is not registered yet.
    /// </summary>
    public static RegistryEntry? ResolveEntry(string? kind)
        => LookupEntry(kind) ?? LookupEntry(FallbackKind);
}
