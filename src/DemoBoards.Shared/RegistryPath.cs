using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Pure path utilities shared by bind resolution and the save lifecycle — a faithful port of
/// <c>registry/lib/path.js</c>. Operates over the loosely-typed data model the registry uses
/// (objects = <see cref="IReadOnlyDictionary{TKey,TValue}"/>, arrays = <see cref="IReadOnlyList{T}"/>,
/// scalars = string / double / bool / null), matching the frontend's plain-object traversal.
/// </summary>
public static class RegistryPath
{
    /// <summary>Port of <c>pathParts(path)</c>: turns <c>a.b[0].c</c> into <c>[a, b, 0, c]</c>.</summary>
    public static IReadOnlyList<string> PathParts(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Array.Empty<string>();
        }

        // Mirror `path.replace(/\[(\d+)\]/g, '.$1').split('.').filter(Boolean)`.
        var parts = new List<string>();
        foreach (string segment in BracketToDot(path).Split('.'))
        {
            if (!string.IsNullOrEmpty(segment))
            {
                parts.Add(segment);
            }
        }

        return parts;
    }

    /// <summary>Port of <c>deepGet(source, path)</c>: traverses a nested object/array by dotted path.</summary>
    public static object? DeepGet(object? source, string? path)
    {
        if (string.IsNullOrEmpty(path) || source is null)
        {
            return null;
        }

        object? current = source;
        foreach (string part in PathParts(path))
        {
            if (current is null)
            {
                return null;
            }

            current = GetMember(current, part);
        }

        return current;
    }

    /// <summary>
    /// Port of <c>deepSet(target, path, value)</c>: returns a shallow-cloned copy of <paramref name="target"/>
    /// with the value at <paramref name="path"/> replaced. The original is never mutated, matching the
    /// frontend's immutable spread-based set.
    /// </summary>
    public static object? DeepSet(object? target, string? path, object? value)
    {
        IReadOnlyList<string> parts = PathParts(path);
        if (parts.Count == 0)
        {
            return target;
        }

        return SetRecursive(target, parts, 0, value);
    }

    private static object? SetRecursive(object? node, IReadOnlyList<string> parts, int index, object? value)
    {
        string part = parts[index];
        bool isLast = index == parts.Count - 1;

        // `Array.isArray(target) ? [...target] : { ...(target ?? {}) }`
        if (node is IReadOnlyList<object?> list)
        {
            var copy = new List<object?>(list);
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int arrayIndex) || arrayIndex < 0)
            {
                return copy;
            }

            while (copy.Count <= arrayIndex)
            {
                copy.Add(null);
            }

            copy[arrayIndex] = isLast
                ? value
                : SetRecursive(copy[arrayIndex], parts, index + 1, value);
            return copy;
        }

        var map = node is IReadOnlyDictionary<string, object?> existing
            ? new Dictionary<string, object?>(existing, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        map[part] = isLast
            ? value
            : SetRecursive(map.TryGetValue(part, out object? child) ? child : null, parts, index + 1, value);
        return map;
    }

    private static object? GetMember(object? current, string part)
    {
        switch (current)
        {
            case IReadOnlyDictionary<string, object?> map:
                return map.TryGetValue(part, out object? value) ? value : null;
            case IReadOnlyList<object?> list:
                return int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    && index >= 0
                    && index < list.Count
                        ? list[index]
                        : null;
            default:
                return null;
        }
    }

    private static string BracketToDot(string path)
    {
        // Replace `[<digits>]` with `.<digits>` (the only bracket form the frontend regex matches).
        var builder = new System.Text.StringBuilder(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '[')
            {
                int j = i + 1;
                while (j < path.Length && char.IsDigit(path[j]))
                {
                    j++;
                }

                if (j > i + 1 && j < path.Length && path[j] == ']')
                {
                    builder.Append('.');
                    builder.Append(path, i + 1, j - (i + 1));
                    i = j;
                    continue;
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
