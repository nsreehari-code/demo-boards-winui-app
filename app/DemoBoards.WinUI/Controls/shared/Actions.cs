using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>One button in an <see cref="Actions"/> row — mirrors the frontend <c>{ id, label?, style?, size?, disabled? }</c>.</summary>
public sealed record ActionButton(string Id, string? Label = null, string? Style = null, string? Size = null, bool Disabled = false);

/// <summary>Mirrors <c>Actions.jsx</c> — a reusable button row. <c>OnAction</c> fires with the button id.</summary>
public sealed record ActionsProps(IReadOnlyList<ActionButton> Buttons, Action<string>? OnAction = null);

public sealed class Actions : Component<ActionsProps>
{
    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);

        if (Props.Buttons is null || Props.Buttons.Count == 0)
        {
            return Empty();
        }

        return HStack(8, Props.Buttons.Select(BuildButton).ToArray());
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
