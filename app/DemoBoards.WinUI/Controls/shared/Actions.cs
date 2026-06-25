using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// One button in an <see cref="Actions"/> row — the typed conversion target the component builds
/// internally from each plain <c>{ id, label?, style?, size?, disabled? }</c> data object.
/// </summary>
public sealed record ActionButton(string Id, string? Label = null, string? Style = null, string? Size = null, bool Disabled = false)
{
    /// <summary>Parses a frontend-shaped button data object into a typed <see cref="ActionButton"/>.</summary>
    public static ActionButton FromData(IReadOnlyDictionary<string, object?>? data)
    {
        IReadOnlyDictionary<string, object?> map = data ?? BoardData.Empty;
        return new ActionButton(
            BoardData.Str(map, "id") ?? string.Empty,
            BoardData.Str(map, "label"),
            BoardData.Str(map, "style"),
            BoardData.Str(map, "size"),
            BoardData.Bool(map, "disabled"));
    }
}

/// <summary>
/// Mirrors <c>Actions.jsx</c> — a reusable button row. <c>Buttons</c> is the plain frontend data array
/// (<c>[{ id, label?, style?, size?, disabled? }]</c>); <c>OnAction</c> fires with the button id.
/// </summary>
public sealed record ActionsProps(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Buttons = null,
    Action<string>? OnAction = null);

public sealed class Actions : Component<ActionsProps>
{
    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);

        if (Props.Buttons is null || Props.Buttons.Count == 0)
        {
            return Empty();
        }

        return HStack(8, Props.Buttons.Select(ActionButton.FromData).Select(BuildButton).ToArray());
    }

    private Element BuildButton(ActionButton button)
    {
        string id = button.Id;
        string label = button.Label ?? button.Id;
        var element = Button(label, () => Props.OnAction?.Invoke(id));

        bool accent = button.Style is "primary" or "success" or "accent";
        var styled = accent ? element.AccentButton() : element.SubtleButton();

        return styled
            .AutomationName(label)
            .Set(control => control.IsEnabled = !button.Disabled);
    }
}
