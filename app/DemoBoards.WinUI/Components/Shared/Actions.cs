using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>Actions.jsx</c> — a reusable button row. <c>Buttons</c> is the plain frontend data array
/// (<c>[{ id, label?, style?, size?, disabled? }]</c>); <c>OnAction</c> fires with the button id. Each
/// entry is converted to an <see cref="ActionButton"/> internally (defined in DemoBoards.Shared).
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
        bool accent = button.Style is "primary" or "success" or "accent";

        return (accent
            ? Button(label, () => Props.OnAction?.Invoke(id)).AccentButton().AutomationName(label)
            : Button(label, () => Props.OnAction?.Invoke(id)).SubtleButton().AutomationName(label))
            .Set(control => control.IsEnabled = !button.Disabled);
    }
}
