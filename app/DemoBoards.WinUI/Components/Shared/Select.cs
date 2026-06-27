using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>SelectControl</c> from <c>Select.jsx</c> — a presentational single-select. Resolves
/// options from a static <c>Options</c> array or a synchronous <c>GetOptions</c> source, normalises
/// object options (<see cref="SelectOption.Normalize"/>) and optionally renders a leading empty option.
/// </summary>
public sealed record SelectControlProps(
    object? Value = null,
    IReadOnlyList<object?>? Options = null,
    Func<object?>? GetOptions = null,
    bool AllowEmpty = false,
    string EmptyLabel = "",
    bool Required = false,
    bool Disabled = false,
    string? AriaLabel = null,
    string? Title = null,
    Action<string>? OnChange = null);

public sealed class SelectControl : Component<SelectControlProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        IReadOnlyList<object?> raw = Props.GetOptions != null
            ? Props.GetOptions() as IReadOnlyList<object?> ?? Array.Empty<object?>()
            : Props.Options ?? Array.Empty<object?>();

        List<SelectOption> options = raw.Select(SelectOption.Normalize).ToList();

        var items = new List<string>();
        if (Props.AllowEmpty)
        {
            items.Add(Props.EmptyLabel);
        }

        items.AddRange(options.Select(option => option.Label));

        string current = Props.Value?.ToString() ?? string.Empty;
        int optionIndex = options.FindIndex(option => option.Value == current);
        int selectedIndex = Props.AllowEmpty
            ? (optionIndex < 0 ? 0 : optionIndex + 1)
            : optionIndex;

        return ComboBox(items.ToArray(), selectedIndex, index =>
            {
                int optionPosition = Props.AllowEmpty ? index - 1 : index;
                if (optionPosition < 0)
                {
                    Props.OnChange?.Invoke(string.Empty);
                }
                else if (optionPosition < options.Count)
                {
                    Props.OnChange?.Invoke(options[optionPosition].Value);
                }
            })
            .Set(comboBox =>
            {
                comboBox.IsEnabled = !Props.Disabled;
                comboBox.Foreground = theme.TextPrimary;
            });
    }
}
