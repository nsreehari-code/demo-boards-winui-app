using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        var numericColumns = columns.Select(column => IsNumericColumn(rows, column)).ToArray();
        GridSize[] columnDefs = columns.Select(_ => GridSize.Star()).ToArray();
        GridSize[] rowDefs = Enumerable.Repeat(GridSize.Auto, rows.Count + 1).ToArray();
        var gridChildren = new List<Element>(columns.Count * (rows.Count + 1));

        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            bool alignRight = numericColumns[columnIndex];
            string column = columns[columnIndex];
            gridChildren.Add(BuildHeaderCell(theme, column, alignRight, Props.Sortable, columnIndex, sortCol, sortDir, setSortCol, setSortDir)
                .Grid(0, columnIndex));
        }

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            IReadOnlyDictionary<string, object?> row = rows[rowIndex];
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                bool alignRight = numericColumns[columnIndex];
                string column = columns[columnIndex];
                gridChildren.Add(BuildDataCell(theme, BoardShared.Stringify(Cell(row, column)), alignRight, rowIndex)
                    .Grid(rowIndex + 1, columnIndex));
            }
        }

        Element tableGrid = Grid(columnDefs, rowDefs, gridChildren.ToArray());

        var sections = new List<Element>
        {
            Border(tableGrid)
                .Background(theme.Transparent)
                .WithBorder(theme.Tables.GridLine, 1)
                .CornerRadius(theme.Tables.Radius)
        };

        if (data.Count > limit)
        {
            sections.Add(
                TextBlock($"Showing {limit} of {data.Count} rows")
                    .FontSize(11)
                    .Foreground(theme.TextMuted));
        }

        return VStack(8, sections.ToArray());
    }

    private static Element BuildHeaderCell(
        AppTheme theme,
        string column,
        bool alignRight,
        bool sortable,
        int columnIndex,
        int sortCol,
        string sortDir,
        Action<int> setSortCol,
        Action<string> setSortDir)
    {
        Element content;
        if (!sortable)
        {
            content = BuildHeaderText(theme, column, alignRight);
        }
        else
        {
            string arrow = sortCol == columnIndex ? (sortDir == "asc" ? " \u2191" : " \u2193") : string.Empty;
            content = Button(column + arrow, () =>
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
                .AutomationName(column)
                .Set(button =>
                {
                    button.Background = null;
                    button.BorderBrush = null;
                    button.Foreground = theme.TextPrimary;
                    button.Padding = new Thickness(0);
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    button.HorizontalContentAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                    button.FontSize = 12;
                });
        }

        return BuildCell(theme, content, isHeader: true, rowIndex: 0, showBottomBorder: true);
    }

    private static Element BuildDataCell(
        AppTheme theme,
        string value,
        bool alignRight,
        int rowIndex) =>
        BuildCell(
            theme,
            TextBlock(value)
                .FontSize(12)
                .Foreground(theme.TextPrimary)
                .Set(text =>
                {
                    text.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    text.HorizontalAlignment = HorizontalAlignment.Stretch;
                }),
            isHeader: false,
            rowIndex: rowIndex,
            showBottomBorder: true);

    private static Element BuildHeaderText(AppTheme theme, string text, bool alignRight) =>
        TextBlock(text.ToUpperInvariant())
            .Bold()
            .FontSize(11)
            .Foreground(theme.Tables.HeaderForeground)
            .Set(block =>
            {
                block.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                block.HorizontalAlignment = HorizontalAlignment.Stretch;
            });

    private static Element BuildCell(AppTheme theme, Element child, bool isHeader, int rowIndex, bool showBottomBorder) =>
        Border(child)
            .Padding(
                theme.Tables.CellPaddingX,
                isHeader ? theme.Tables.HeaderPaddingY : theme.Tables.CellPaddingY,
                theme.Tables.CellPaddingX,
                isHeader ? theme.Tables.HeaderPaddingY : theme.Tables.CellPaddingY)
            .Background(isHeader ? theme.Tables.HeaderBackground : rowIndex % 2 == 1 ? theme.Tables.RowStripeBackground : theme.Transparent)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Set(border =>
            {
                border.BorderBrush = theme.Tables.GridLine;
                border.BorderThickness = showBottomBorder ? new Thickness(0, 0, 0, 1) : new Thickness(0);
            });

    private static bool IsNumericColumn(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string key)
        => rows.Select(row => Cell(row, key)).Where(value => value != null).All(value => BoardShared.AsNumber(value) is not null);

    private static object? Cell(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? value) ? value : null;
}
