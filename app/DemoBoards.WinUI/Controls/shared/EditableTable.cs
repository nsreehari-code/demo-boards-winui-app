using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

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

        var headerCells = new List<Element>(columns
            .Select(column => (Element)TextBlock(column).Bold().FontSize(12).Foreground(theme.TextPrimary).Flex(grow: 1)));
        if (allowDeleteRow)
        {
            headerCells.Add(TextBlock(string.Empty).Width(28));
        }

        var body = new List<Element> { HStack(8, headerCells.ToArray()) };

        if (rows.Count == 0)
        {
            body.Add(TextBlock(placeholder).Opacity(0.6).Foreground(theme.TextPrimary));
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
                    return (Element)TextBox(current, text =>
                        {
                            var next = BoardShared.MergeRows(rows);
                            next[capturedRow][column] = isNumber ? BoardShared.AsNumber(text) ?? 0d : text;
                            Commit(next);
                        }).AutomationName($"{column} {capturedRow + 1}").Flex(grow: 1);
                }));

                if (allowDeleteRow)
                {
                    cells.Add(Button("\u2715", () => Commit(rows.Where((_, index) => index != capturedRow).Select(r => new Dictionary<string, object?>(r, StringComparer.Ordinal)).ToList()))
                        .SubtleButton().AutomationName("Remove row").Width(28));
                }

                body.Add(HStack(8, cells.ToArray()));
            }
        }

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

        body.Add(HStack(8, actions.ToArray()));

        return Border(VStack(6, body.ToArray()))
            .Padding(10)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12);
    }
}
