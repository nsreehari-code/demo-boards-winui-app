using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>PanelVertical.jsx</c> — a full-height side rail that composes a
/// <see cref="FloatingCircularButton"/> toggle with a scrollable panel. <c>Expanded</c> drives whether
/// the panel content shows; <c>FabPosition</c> selects which corner the FAB pins to.
/// </summary>
public sealed record PanelVerticalProps(
    string FabPosition = "top-left",
    bool Expanded = false,
    Action? OnToggle = null,
    string? AriaLabel = null,
    string? Title = null,
    string? Icon = null,
    string? IconToggled = null,
    Element? Children = null);

public sealed class PanelVertical : Component<PanelVerticalProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        Element fab = Component<FloatingCircularButton, FloatingCircularButtonProps>(
            new FloatingCircularButtonProps(
                Toggled: Props.Expanded,
                Icon: Props.Icon,
                IconToggled: Props.IconToggled,
                OnClick: () => Props.OnToggle?.Invoke(),
                OnClickToggled: () => Props.OnToggle?.Invoke(),
                AriaLabel: Props.AriaLabel ?? Props.Title ?? "Toggle panel"));

        if (!Props.Expanded)
        {
            return fab;
        }

        Element panel = Border(VStack(8,
                Props.Title != null
                    ? (Element)TextBlock(Props.Title).Bold().Foreground(theme.TextPrimary)
                    : Empty(),
                ScrollViewer(Props.Children ?? Empty()).Flex(grow: 1)))
            .Padding(12)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(12)
            .MinWidth(280)
            .AutomationName(Props.AriaLabel ?? Props.Title ?? "Panel");

        return VStack(8, panel, fab);
    }
}
