using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
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
            Element[] entries = map.Select(pair => (Element)HStack(8,
                    TextBlock(pair.Key).FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary).Flex(grow: 1),
                    TextBlock(pair.Value != null ? BoardShared.Stringify(pair.Value) : "\u2014").FontSize(12).Foreground(theme.TextPrimary).Flex(grow: 1)))
                .ToArray();
            return VStack(4, entries);
        }

        return TextBlock(BoardShared.Stringify(Props.Data)).FontSize(12).Foreground(theme.TextPrimary);
    }
}
