using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>EditableTable.jsx</c> — a self-contained editable grid. Owns its own draft rows (layered
/// over <c>BaseRows</c>), derives columns from the spec / the row keys, and exposes inline cell
/// editing plus add/delete-row and dirty-gated Discard / Save actions. Configuration travels as the
/// plain <c>spec</c> data object on <see cref="EditableTableProps.Spec"/> — converted internally via
/// <see cref="EditableTableSpec.FromData"/> (defined in DemoBoards.Shared).
/// </summary>
public sealed record EditableTableProps(
    IReadOnlyDictionary<string, object?>? Spec = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? BaseRows = null,
    Action<IReadOnlyList<IReadOnlyDictionary<string, object?>>>? OnSave = null);

public sealed class EditableTable : Component<EditableTableProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (rows, setRows) = UseState(BoardShared.MergeRows(Props.BaseRows));
        var (dirty, setDirty) = UseState(false);

        EditableTableSpec spec = EditableTableSpec.FromData(Props.Spec);
        IReadOnlyDictionary<string, FieldSchema> schemaProps = spec.Schema?.Properties ?? new Dictionary<string, FieldSchema>();
        bool allowAddRow = spec.AllowAddRow;
        bool allowDeleteRow = spec.AllowDeleteRow;
        string placeholder = spec.Placeholder;
        IReadOnlyList<string> columns = BoardShared.GetObjectColumns(rows, spec.Columns);

        void Commit(List<Dictionary<string, object?>> next)
        {
            setRows(next);
            setDirty(true);
        }

        if (columns.Count == 0 && !allowAddRow)
        {
            return TextBlock(placeholder).Opacity(0.7).Foreground(theme.TextPrimary);
        }

        bool[] numericColumns = columns.Select(column => IsNumericColumn(rows, schemaProps, column)).ToArray();
        GridSize[] columnDefs = columns.Select(_ => GridSize.Star()).ToArray();
        if (allowDeleteRow)
        {
            columnDefs = columnDefs.Concat(new[] { GridSize.Px(32) }).ToArray();
        }

        int visibleRowCount = Math.Max(rows.Count, 1);
        GridSize[] rowDefs = Enumerable.Repeat(GridSize.Auto, visibleRowCount + 1).ToArray();
        var gridChildren = new List<Element>();

        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            gridChildren.Add(BuildHeaderCell(theme, columns[columnIndex], numericColumns[columnIndex])
                .Grid(0, columnIndex));
        }

        if (allowDeleteRow)
        {
            gridChildren.Add(BuildHeaderSpacerCell(theme).Grid(0, columns.Count));
        }

        if (rows.Count == 0)
        {
            gridChildren.Add(
                Border(
                    TextBlock(placeholder)
                        .FontSize(12)
                        .Foreground(theme.TextMuted)
                        .Set(block =>
                        {
                            block.HorizontalAlignment = HorizontalAlignment.Stretch;
                            block.TextAlignment = TextAlignment.Center;
                        }))
                    .Padding(theme.Tables.CellPaddingX, theme.Tables.CellPaddingY, theme.Tables.CellPaddingX, theme.Tables.CellPaddingY)
                    .Background(theme.Transparent)
                    .Set(border =>
                    {
                        border.BorderBrush = theme.Tables.GridLine;
                        border.BorderThickness = new Thickness(0, 0, 0, 1);
                    })
                    .Grid(1, 0)
                    .WrapGridColumnSpan(allowDeleteRow ? columns.Count + 1 : columns.Count));
        }
        else
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                int capturedRow = rowIndex;
                Dictionary<string, object?> row = rows[rowIndex];
                var cells = new List<Element>(columns.Select(column =>
                {
                    FieldSchema prop = schemaProps.TryGetValue(column, out FieldSchema? schema) ? schema : new FieldSchema();
                    bool isNumber = prop.Type is "number" or "integer" || row.TryGetValue(column, out object? cell) && cell is double or int or long or float or decimal;
                    string current = row.TryGetValue(column, out object? value) ? BoardShared.Stringify(value) : string.Empty;
                    return (Element)BuildEditorCell(theme, current, isNumber, rowIndex, column, text =>
                        {
                            var next = BoardShared.MergeRows(rows);
                            next[capturedRow][column] = isNumber ? BoardShared.AsNumber(text) ?? 0d : text;
                            Commit(next);
                        });
                }));

                for (int columnIndex = 0; columnIndex < cells.Count; columnIndex++)
                {
                    gridChildren.Add(cells[columnIndex].Grid(rowIndex + 1, columnIndex));
                }

                if (allowDeleteRow)
                {
                    gridChildren.Add(BuildDeleteCell(theme, () => Commit(rows.Where((_, index) => index != capturedRow).Select(r => new Dictionary<string, object?>(r, StringComparer.Ordinal)).ToList()), rowIndex)
                        .Grid(rowIndex + 1, columns.Count));
                }
            }
        }

        Element tableSurface = Border(Grid(columnDefs, rowDefs, gridChildren.ToArray()))
            .Background(theme.Transparent)
            .WithBorder(theme.Tables.GridLine, 1)
            .CornerRadius(theme.Tables.Radius);

        var actions = new List<Element>();
        if (allowAddRow)
        {
            actions.Add(Button("+ Add row", () =>
                {
                    var next = BoardShared.MergeRows(rows);
                    var blank = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (string column in columns)
                    {
                        blank[column] = string.Empty;
                    }

                    next.Add(blank);
                    Commit(next);
                }).SubtleButton().AutomationName("Add row"));
        }

        if (dirty)
        {
            actions.Add(Button("Discard", () =>
                {
                    setRows(BoardShared.MergeRows(Props.BaseRows));
                    setDirty(false);
                }).SubtleButton().AutomationName("Discard changes"));
            actions.Add(Button("Save", () =>
                {
                    Props.OnSave?.Invoke(rows.Select(r => (IReadOnlyDictionary<string, object?>)r).ToList());
                    setDirty(false);
                }).AccentButton().AutomationName("Save changes"));
        }

        if (actions.Count > 0)
        {
            return VStack(8, tableSurface, HStack(6, actions.ToArray()));
        }

        return VStack(8, tableSurface);
    }

    private static Element BuildHeaderCell(AppTheme theme, string text, bool alignRight) =>
        Border(
            TextBlock(text.ToUpperInvariant())
                .Bold()
                .FontSize(11)
                .Foreground(theme.Tables.HeaderForeground)
                .Set(block =>
                {
                    block.HorizontalAlignment = HorizontalAlignment.Stretch;
                    block.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                }))
            .Padding(theme.Tables.CellPaddingX, theme.Tables.HeaderPaddingY, theme.Tables.CellPaddingX, theme.Tables.HeaderPaddingY)
            .Background(theme.Tables.HeaderBackground)
            .Set(border =>
            {
                border.BorderBrush = theme.Tables.GridLine;
                border.BorderThickness = new Thickness(0, 0, 0, 1);
            });

    private static Element BuildHeaderSpacerCell(AppTheme theme) =>
        Border(Empty())
            .Background(theme.Tables.HeaderBackground)
            .Set(border =>
            {
                border.BorderBrush = theme.Tables.GridLine;
                border.BorderThickness = new Thickness(0, 0, 0, 1);
            });

    private static Element BuildEditorCell(AppTheme theme, string current, bool isNumber, int rowIndex, string column, Action<string> onChange) =>
        Border(
            TextBox(current, onChange)
                .AutomationName($"{column} {rowIndex + 1}")
                .Set(textBox =>
                {
                    textBox.Background = theme.Transparent;
                    textBox.BorderThickness = new Thickness(0);
                    textBox.Padding = new Thickness(0);
                    textBox.FontSize = 12;
                    textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                    textBox.TextAlignment = isNumber ? TextAlignment.Right : TextAlignment.Left;
                }))
            .Padding(theme.Tables.CellPaddingX, theme.Tables.CellPaddingY, theme.Tables.CellPaddingX, theme.Tables.CellPaddingY)
            .Background(rowIndex % 2 == 1 ? theme.Tables.RowStripeBackground : theme.Transparent)
            .Set(border =>
            {
                border.BorderBrush = theme.Tables.GridLine;
                border.BorderThickness = new Thickness(0, 0, 0, 1);
            });

    private static Element BuildDeleteCell(AppTheme theme, Action onDelete, int rowIndex) =>
        Border(
            Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.X, 13)), onDelete)
                .SubtleButton()
                .AutomationName("Remove row")
                .Set(button =>
                {
                    button.Foreground = theme.StatusError;
                    button.Padding = new Thickness(0);
                    button.BorderBrush = null;
                    button.Background = null;
                }))
            .Padding(6, theme.Tables.CellPaddingY, 6, theme.Tables.CellPaddingY)
            .Background(rowIndex % 2 == 1 ? theme.Tables.RowStripeBackground : theme.Transparent)
            .Set(border =>
            {
                border.BorderBrush = theme.Tables.GridLine;
                border.BorderThickness = new Thickness(0, 0, 0, 1);
            });

    private static bool IsNumericColumn(IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyDictionary<string, FieldSchema> schemaProps, string column)
    {
        if (schemaProps.TryGetValue(column, out FieldSchema? schema) && schema.Type is "number" or "integer")
        {
            return true;
        }

        return rows.Select(row => row.TryGetValue(column, out object? value) ? value : null)
            .Where(value => value != null)
            .All(value => value is double or int or long or float or decimal || BoardShared.AsNumber(value) is not null);
    }
}
