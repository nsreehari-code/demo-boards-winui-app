using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>Table.jsx</c> — a read-only data table with click-to-sort columns. Rows are dictionaries
/// (object rows); columns are derived from the rows unless <c>Columns</c> is supplied.
/// </summary>
public sealed record TableProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Data,
    IReadOnlyList<string>? Columns = null,
    int MaxRows = 200,
    bool Sortable = true,
    string Placeholder = "No data");

public sealed class Table : Component<TableProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (sortCol, setSortCol) = UseState(-1);
        var (sortDir, setSortDir) = UseState("asc");

        IReadOnlyList<IReadOnlyDictionary<string, object?>>? data = Props.Data;
        if (data is null || data.Count == 0)
        {
            return TextBlock(Props.Placeholder).FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary);
        }

        int limit = Math.Min(data.Count, Props.MaxRows);
        List<IReadOnlyDictionary<string, object?>> rows = data.Take(limit).ToList();
        IReadOnlyList<string> columns = BoardShared.GetObjectColumns(rows, Props.Columns);

        if (Props.Sortable && sortCol >= 0 && sortCol < columns.Count)
        {
            string key = columns[sortCol];
            string dir = sortDir;
            rows = rows.OrderBy(row => row, Comparer<IReadOnlyDictionary<string, object?>>.Create((left, right) =>
            {
                object? leftValue = Cell(left, key);
                object? rightValue = Cell(right, key);
                if (leftValue is null)
                {
                    return 1;
                }

                if (rightValue is null)
                {
                    return -1;
                }

                double? leftNumber = BoardShared.AsNumber(leftValue);
                double? rightNumber = BoardShared.AsNumber(rightValue);
                int compared = leftNumber.HasValue && rightNumber.HasValue
                    ? leftNumber.Value.CompareTo(rightNumber.Value)
                    : string.Compare(BoardShared.Stringify(leftValue), BoardShared.Stringify(rightValue), StringComparison.Ordinal);
                return dir == "asc" ? compared : -compared;
            })).ToList();
        }

        Element header = HStack(6, columns.Select((column, index) =>
        {
            if (!Props.Sortable)
            {
                return (Element)TextBlock(column).Bold().FontSize(12).Foreground(theme.TextPrimary).Flex(grow: 1);
            }

            string arrow = sortCol == index ? (sortDir == "asc" ? " \u2191" : " \u2193") : string.Empty;
            int columnIndex = index;
            return (Element)Button(column + arrow, () =>
                {
                    if (sortCol == columnIndex)
                    {
                        setSortDir(sortDir == "asc" ? "desc" : "asc");
                    }
                    else
                    {
                        setSortCol(columnIndex);
                        setSortDir("asc");
                    }
                })
                .SubtleButton()
                .Flex(grow: 1);
        }).ToArray());

        Element[] bodyRows = rows.Select(row => (Element)HStack(6, columns
            .Select(column => (Element)TextBlock(BoardShared.Stringify(Cell(row, column)))
                .FontSize(12)
                .Foreground(theme.TextPrimary)
                .Flex(grow: 1))
            .ToArray())).ToArray();

        var sections = new List<Element> { header };
        sections.AddRange(bodyRows);
        if (data.Count > limit)
        {
            sections.Add(TextBlock($"Showing {limit} of {data.Count} rows").FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary));
        }

        return VStack(4, sections.ToArray());
    }

    private static object? Cell(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? value) ? value : null;
}
