using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>FloatingCircularButton.jsx</c> — a controlled circular toggle. When <c>Toggled</c>, it
/// shows <c>IconToggled</c> (falling back to <c>Icon</c>) and fires <c>OnClickToggled</c> (falling back
/// to <c>OnClick</c>). Icon strings mirror the frontend Bootstrap-icon names (the <c>bi-</c> prefix is
/// dropped for display).
/// </summary>
public sealed record FloatingCircularButtonProps(
    bool Toggled = false,
    string? Icon = null,
    string? IconToggled = null,
    Action? OnClick = null,
    Action? OnClickToggled = null,
    string? AriaLabel = null);

public sealed class FloatingCircularButton : Component<FloatingCircularButtonProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        string? activeIcon = Props.Toggled ? Props.IconToggled ?? Props.Icon : Props.Icon;
        Action? activeOnClick = Props.Toggled ? Props.OnClickToggled ?? Props.OnClick : Props.OnClick;
        string glyph = string.IsNullOrEmpty(activeIcon) ? "\u2261" : activeIcon!.Replace("bi-", string.Empty);

        return Button(glyph, () => activeOnClick?.Invoke())
            .AccentButton()
            .AutomationName(Props.AriaLabel ?? "Toggle")
            .Foreground(theme.TextPrimary)
            .Set(button =>
            {
                button.Width = 44;
                button.Height = 44;
                button.CornerRadius = new CornerRadius(22);
                button.Padding = new Thickness(0);
            });
    }
}
