using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI;
using Windows.UI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Shared, framework-agnostic helpers backing the ported <c>components/shared</c> family. These mirror
/// the frontend's <c>registry/lib</c> utilities (column derivation, row cloning, chart normalisation)
/// plus the semantic tone palette the CSS <c>board-tone--*</c> classes encoded. Keeping them in one
/// place lets each ported component stay a thin, declarative Reactor render.
/// </summary>
internal static class BoardShared
{
    /// <summary>Semantic chart palette — mirrors <c>CHART_PALETTE</c> from <c>registry/lib/chart.js</c>.</summary>
    public static readonly IReadOnlyList<Color> ChartPalette = new[]
    {
        FromHex("#4e79a7"), FromHex("#f28e2b"), FromHex("#e15759"), FromHex("#76b7b2"), FromHex("#59a14f"),
        FromHex("#edc948"), FromHex("#b07aa1"), FromHex("#ff9da7"), FromHex("#9c755f"), FromHex("#bab0ac"),
    };

    /// <summary>
    /// Maps a semantic tone key (and the Bootstrap-ish aliases the frontend accepted) to a concrete
    /// colour. Mirrors <c>TONE_CLASS</c> in <c>Badge.jsx</c> / the <c>board-tone--*</c> CSS roles.
    /// </summary>
    public static Color ToneColor(string? tone) => (tone ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "green" or "success" => FromHex("#3aa856"),
        "amber" or "warning" => FromHex("#d99a2b"),
        "red" or "danger" or "failed" => FromHex("#e05a5a"),
        "blue" or "primary" or "running" => FromHex("#3a82c4"),
        "secondary" or "unknown" or "" => FromHex("#8a8f98"),
        _ => FromHex("#8a8f98"),
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

    /// <summary>Best-effort numeric coercion used by the chart/series paths.</summary>
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

    private static Color FromHex(string hex)
    {
        string value = hex.TrimStart('#');
        byte r = Convert.ToByte(value.Substring(0, 2), 16);
        byte g = Convert.ToByte(value.Substring(2, 2), 16);
        byte b = Convert.ToByte(value.Substring(4, 2), 16);
        return Color.FromArgb(0xFF, r, g, b);
    }
}

/// <summary>
/// A single select option — mirrors the frontend's <c>normalizeOption</c> output (<c>{ value, label }</c>).
/// Inputs may be a scalar or an object exposing <c>value/id/label/title</c>.
/// </summary>
public sealed record SelectOption(string Value, string Label)
{
    /// <summary>Port of <c>normalizeOption(option)</c> from <c>Select.jsx</c>.</summary>
    public static SelectOption Normalize(object? option)
    {
        if (option is SelectOption ready)
        {
            return ready;
        }

        if (option is IReadOnlyDictionary<string, object?> map)
        {
            string value = BoardShared.Stringify(
                Pick(map, "value") ?? Pick(map, "id") ?? Pick(map, "label") ?? string.Empty);
            string label = BoardShared.Stringify(
                Pick(map, "label") ?? Pick(map, "title") ?? Pick(map, "value") ?? Pick(map, "id") ?? string.Empty);
            return new SelectOption(value, label);
        }

        string scalar = BoardShared.Stringify(option);
        return new SelectOption(scalar, scalar);
    }

    private static object? Pick(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) ? value : null;
}

/// <summary>
/// A JSON-Schema-ish field descriptor shared by <c>Form</c>, <c>EditableTable</c>, <c>Searchbox</c> and
/// <c>Select</c>. Mirrors the <c>spec.*.properties.&lt;key&gt;</c> shapes those frontend components read.
/// </summary>
public sealed record FieldSchema(
    string? Type = null,
    string? Format = null,
    string? Title = null,
    string? Description = null,
    string? Hint = null,
    string? Placeholder = null,
    IReadOnlyList<object?>? Enum = null,
    IReadOnlyList<string>? EnumNames = null,
    IReadOnlyList<OneOfEntry>? OneOf = null,
    IReadOnlyList<object?>? Options = null,
    FieldSchema? Items = null,
    double? Minimum = null,
    double? Maximum = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    bool ReadOnly = false,
    bool Disabled = false,
    int? ColSpan = null,
    int? Rows = null,
    bool Multiline = false,
    Func<object?>? GetOptions = null);

/// <summary>A labelled enum entry — mirrors JSON Schema <c>oneOf: [{ const, title }]</c>.</summary>
public sealed record OneOfEntry(object? Const, string? Title = null);
