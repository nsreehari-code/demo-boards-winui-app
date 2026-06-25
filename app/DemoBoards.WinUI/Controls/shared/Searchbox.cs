using System;
using System.Globalization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>Searchbox.jsx</c> — a self-contained search input. Owns its journal and type-aware
/// coercion; <c>OnSubmit</c> fires with the coerced value. <c>Prop</c> is the resolved field schema.
/// </summary>
public sealed record SearchboxProps(
    FieldSchema Prop,
    string? FieldKey = null,
    object? Value = null,
    bool IsRequired = false,
    string ButtonLabel = "Search",
    Action<object?>? OnSubmit = null);

public sealed class Searchbox : Component<SearchboxProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (journal, setJournal) = UseState(Props.Value?.ToString() ?? string.Empty);

        void Submit()
        {
            if (string.IsNullOrEmpty(Props.FieldKey))
            {
                return;
            }

            object? next = journal;
            if (Props.Prop.Type is "number" or "integer")
            {
                next = journal.Length == 0
                    ? string.Empty
                    : double.TryParse(journal, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) ? parsed : journal;
            }

            Props.OnSubmit?.Invoke(next);
        }

        string placeholder = Props.Prop.Placeholder ?? Props.Prop.Title ?? Props.FieldKey ?? string.Empty;

        Element input = TextBox(journal, setJournal)
            .AutomationName(Props.Prop.Title ?? Props.FieldKey ?? "Search")
            .PlaceholderText(placeholder)
            .Foreground(theme.TextPrimary)
            .Flex(grow: 1);

        Element submit = Button(Props.ButtonLabel, Submit)
            .SubtleButton()
            .AutomationName(Props.ButtonLabel);

        return HStack(8, input, submit);
    }
}
