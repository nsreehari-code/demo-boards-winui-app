using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>List.jsx</c> — a read-only renderer that adapts to the data shape: primitive arrays
/// become a bullet list, object arrays reuse <see cref="Table"/>, plain objects become a definition
/// list, and scalars render as plain text.
/// </summary>
public sealed record ListProps(
    object? Data,
    IReadOnlyList<string>? Columns = null,
    int? MaxRows = null,
    bool Sortable = true,
    string Placeholder = "Empty",
    string TablePlaceholder = "No data");

public sealed class List : Component<ListProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        if (Props.Data is null)
        {
            return Empty();
        }

        if (Props.Data is IReadOnlyList<object?> array)
        {
            if (array.Count == 0)
            {
                return TextBlock(Props.Placeholder).FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary);
            }

            if (array[0] is string or int or long or double or float or decimal)
            {
                int max = Props.MaxRows ?? array.Count;
                Element[] bullets = array.Take(max)
                    .Select(item => (Element)TextBlock($"\u2022 {BoardShared.Stringify(item)}").FontSize(12).Foreground(theme.TextPrimary))
                    .ToArray();
                return VStack(4, bullets);
            }

            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = array
                .OfType<IReadOnlyDictionary<string, object?>>()
                .ToList();
            return Component<Table, TableProps>(new TableProps(
                Data: rows,
                Columns: Props.Columns,
                MaxRows: Props.MaxRows ?? 200,
                Sortable: Props.Sortable,
                Placeholder: Props.TablePlaceholder));
        }

        if (Props.Data is IReadOnlyDictionary<string, object?> map)
        {
            GridSize[] columnDefs = [GridSize.Star(5), GridSize.Star(7)];
            GridSize[] rowDefs = Enumerable.Repeat(GridSize.Auto, map.Count).ToArray();
            var children = new List<Element>(map.Count * 2);
            int rowIndex = 0;
            foreach (KeyValuePair<string, object?> pair in map)
            {
                bool showBottomBorder = rowIndex < map.Count - 1;
                children.Add(BuildDefinitionCell(
                        theme,
                        TextBlock(pair.Key)
                            .FontSize(12)
                            .Foreground(theme.TextMuted)
                            .Set(block =>
                            {
                                block.TextWrapping = TextWrapping.NoWrap;
                                block.TextTrimming = TextTrimming.CharacterEllipsis;
                            }),
                        rowIndex,
                        showBottomBorder)
                    .Grid(rowIndex, 0));
                children.Add(BuildDefinitionCell(
                        theme,
                        TextBlock(pair.Value != null ? BoardShared.Stringify(pair.Value) : "\u2014")
                            .FontSize(12)
                            .Foreground(theme.TextPrimary)
                            .Set(block => block.TextWrapping = TextWrapping.WrapWholeWords),
                        rowIndex,
                        showBottomBorder)
                    .Grid(rowIndex, 1));
                rowIndex++;
            }

            return Grid(columnDefs, rowDefs, children.ToArray());
        }

        return TextBlock(BoardShared.Stringify(Props.Data)).FontSize(12).Foreground(theme.TextPrimary);
    }

    private static Element BuildDefinitionCell(AppTheme theme, Element child, int rowIndex, bool showBottomBorder) =>
        Border(child)
            .Padding(0, 2, 0, 2)
            .Background(rowIndex % 2 == 1 ? theme.Tables.RowStripeBackground : theme.Transparent)
            .Set(border =>
            {
                border.BorderBrush = theme.Tables.GridLine;
                border.BorderThickness = showBottomBorder ? new Thickness(0, 0, 0, 1) : new Thickness(0);
            });
}
