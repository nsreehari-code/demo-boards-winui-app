using System;
using System.Collections.Generic;
using Microsoft.UI;
using Windows.UI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// WinUI-only board helpers: the semantic colour palette / tone mapping that needs <see cref="Color"/>,
/// plus thin forwarders to the framework-agnostic <see cref="BoardValues"/> so existing call sites keep
/// using <c>BoardShared.*</c>. The plain-data → typed-record converters and map readers now live in the
/// DemoBoards.Shared library (<see cref="BoardData"/>, <see cref="FieldSchema"/>, the spec records).
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

    /// <summary>Forwards to <see cref="BoardValues.GetObjectColumns"/> (kept for call-site stability).</summary>
    public static IReadOnlyList<string> GetObjectColumns(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<string>? configuredColumns) => BoardValues.GetObjectColumns(rows, configuredColumns);

    /// <summary>Forwards to <see cref="BoardValues.MergeRows"/>.</summary>
    public static List<Dictionary<string, object?>> MergeRows(IEnumerable<IReadOnlyDictionary<string, object?>>? rows) =>
        BoardValues.MergeRows(rows);

    /// <summary>Forwards to <see cref="BoardValues.Stringify"/>.</summary>
    public static string Stringify(object? value) => BoardValues.Stringify(value);

    /// <summary>Forwards to <see cref="BoardValues.AsNumber"/>.</summary>
    public static double? AsNumber(object? value) => BoardValues.AsNumber(value);

    private static Color FromHex(string hex)
    {
        string value = hex.TrimStart('#');
        byte r = Convert.ToByte(value.Substring(0, 2), 16);
        byte g = Convert.ToByte(value.Substring(2, 2), 16);
        byte b = Convert.ToByte(value.Substring(4, 2), 16);
        return Color.FromArgb(0xFF, r, g, b);
    }
}
