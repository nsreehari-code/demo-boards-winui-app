using System;
using System.Collections.Generic;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// WinUI-only board helpers: the semantic colour palette / tone mapping that needs <see cref="Color"/>,
/// plus thin forwarders to the framework-agnostic <see cref="BoardValues"/> so existing call sites keep
/// using <c>BoardShared.*</c>. The plain-data → typed-record converters and map readers now live in the
/// DemoBoards.Shared library (<see cref="BoardData"/>, <see cref="FieldSchema"/>, the spec records).
/// </summary>
internal static class BoardShared
{
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
}
