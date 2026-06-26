using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Pure value helpers shared by the ported <c>components/shared</c> family — mirrors the frontend's
/// <c>registry/lib</c> utilities (column derivation, row cloning, scalar stringification, numeric
/// coercion). No UI dependency, so they live in the framework-agnostic data layer and back both the
/// WinUI components and the converter parity tests.
/// </summary>
public static class BoardValues
{
    /// <summary>Renders an arbitrary cell value the way the frontend's <c>String(value)</c> did.</summary>
    public static string Stringify(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Best-effort numeric coercion used by the chart/series/number paths.</summary>
    public static double? AsNumber(object? value) => value switch
    {
        null => null,
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => null,
    };

    /// <summary>Port of <c>getObjectColumns(rows, configuredColumns)</c> from <c>fieldConfig.js</c>.</summary>
    public static IReadOnlyList<string> GetObjectColumns(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<string>? configuredColumns)
    {
        if (configuredColumns is { Count: > 0 })
        {
            return configuredColumns;
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            if (row is null)
            {
                continue;
            }

            foreach (string key in row.Keys)
            {
                if (seen.Add(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>Port of <c>mergeRows(rows)</c> — shallow-clones each row into an editable dictionary.</summary>
    public static List<Dictionary<string, object?>> MergeRows(IEnumerable<IReadOnlyDictionary<string, object?>>? rows)
    {
        if (rows is null)
        {
            return new List<Dictionary<string, object?>>();
        }

        return rows
            .Select(row => row is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(row, StringComparer.Ordinal))
            .ToList();
    }
}

/// <summary>
/// Small readers that coerce a loosely-typed data map (the plain-object props the components accept)
/// into the scalars, lists and callbacks the typed records need. Keeps the data → record conversion
/// in one place so every component parses props the same way.
/// </summary>
public static class BoardData
{
    public static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, object?> AsMap(object? value) =>
        value as IReadOnlyDictionary<string, object?> ?? Empty;

    public static object? Get(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) ? value : null;

    public static string? Str(IReadOnlyDictionary<string, object?> map, string key)
    {
        object? value = Get(map, key);
        return value is null ? null : BoardValues.Stringify(value);
    }

    public static bool Bool(IReadOnlyDictionary<string, object?> map, string key) =>
        Get(map, key) switch
        {
            bool b => b,
            string s when bool.TryParse(s, out bool parsed) => parsed,
            _ => false,
        };

    public static bool BoolOr(IReadOnlyDictionary<string, object?> map, string key, bool fallback) =>
        Get(map, key) switch
        {
            bool b => b,
            string s when bool.TryParse(s, out bool parsed) => parsed,
            _ => fallback,
        };

    public static double? Dbl(IReadOnlyDictionary<string, object?> map, string key) =>
        BoardValues.AsNumber(Get(map, key));

    public static int? Int(IReadOnlyDictionary<string, object?> map, string key)
    {
        double? value = BoardValues.AsNumber(Get(map, key));
        return value.HasValue ? (int)Math.Round(value.Value) : null;
    }

    public static IReadOnlyList<object?>? List(IReadOnlyDictionary<string, object?> map, string key) =>
        ToList(Get(map, key));

    public static IReadOnlyList<object?>? ToList(object? value)
    {
        switch (value)
        {
            case null:
            case string:
                return null;
            case IReadOnlyList<object?> ready:
                return ready;
            case System.Collections.IEnumerable seq:
                var list = new List<object?>();
                foreach (object? item in seq)
                {
                    list.Add(item);
                }

                return list;
            default:
                return null;
        }
    }

    public static IReadOnlyList<string>? StrList(IReadOnlyDictionary<string, object?> map, string key) =>
        List(map, key)?.Select(BoardValues.Stringify).ToList();

    public static Func<object?>? Func(IReadOnlyDictionary<string, object?> map, string key) =>
        Get(map, key) as Func<object?>;
}
