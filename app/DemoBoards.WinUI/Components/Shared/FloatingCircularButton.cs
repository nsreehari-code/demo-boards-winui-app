using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using System.Collections.Generic;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

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
    /// <summary>Maps the frontend Bootstrap-icon names this control receives to host SVG assets.</summary>
    private static readonly Dictionary<string, string> IconSources = new(StringComparer.Ordinal)
    {
        ["bi-chevron-left"] = HostIconSources.ChevronLeft,
        ["bi-chevron-right"] = HostIconSources.ChevronRight,
        ["bi-chevron-up"] = HostIconSources.ChevronUp,
        ["bi-chevron-down"] = HostIconSources.ChevronDown,
        ["bi-gear-fill"] = HostIconSources.GearFill,
        ["bi-x-lg"] = HostIconSources.XLg,
        ["bi-flask"] = HostIconSources.Flask,
        ["bi-compass"] = HostIconSources.Compass,
        ["bi-diagram-3"] = HostIconSources.Diagram3,
        ["bi-bounding-box"] = HostIconSources.BoundingBox,
        ["bi-list"] = HostIconSources.List,
    };

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        string? activeIcon = Props.Toggled ? Props.IconToggled ?? Props.Icon : Props.Icon;
        Action? activeOnClick = Props.Toggled ? Props.OnClickToggled ?? Props.OnClick : Props.OnClick;
        string source = activeIcon is not null && IconSources.TryGetValue(activeIcon, out string? mapped)
            ? mapped
            : HostIconSources.List;

        return Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(source, 20)), () => activeOnClick?.Invoke())
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
