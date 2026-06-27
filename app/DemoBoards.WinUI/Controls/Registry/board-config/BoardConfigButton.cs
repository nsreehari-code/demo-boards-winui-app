using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>BoardConfigButton.jsx</c> — a standardized action button used across the board settings modal.
/// Supports variants (secondary/primary/plain), Bootstrap icons, and custom icon nodes.
/// </summary>
public sealed record BoardConfigButtonProps(
    string Variant = "secondary",
    string? Icon = null,
    Element? IconNode = null,
    string? Label = null,
    Action? OnClick = null,
    bool Disabled = false,
    string? Title = null,
    string? AutomationName = null);

public sealed class BoardConfigButton : Component<BoardConfigButtonProps>
{
    /// <summary>Maps the frontend's Bootstrap icon class names to ported SVG assets.</summary>
    private static readonly Dictionary<string, string> IconSources = new(StringComparer.Ordinal)
    {
        ["bi-flask"] = HostIconSources.Flask,
        ["bi-compass"] = HostIconSources.Compass,
        ["bi-diagram-3"] = HostIconSources.Diagram3,
        ["bi-bounding-box"] = HostIconSources.BoundingBox,
        ["bi-brightness-high"] = HostIconSources.BrightnessHigh,
        ["bi-moon-stars"] = HostIconSources.MoonStars,
        ["bi-gear-fill"] = HostIconSources.GearFill,
        ["bi-x-lg"] = HostIconSources.XLg,
        ["bi-chevron-left"] = HostIconSources.ChevronLeft,
        ["bi-chevron-right"] = HostIconSources.ChevronRight,
        ["bi-chevron-up"] = HostIconSources.ChevronUp,
        ["bi-chevron-down"] = HostIconSources.ChevronDown,
        ["bi-list"] = HostIconSources.List,
    };

    public override Element Render()
    {
        Element? renderedIcon = Props.IconNode ?? (
            string.IsNullOrEmpty(Props.Icon)
                ? null
                : IconSources.TryGetValue(Props.Icon, out var iconSource)
                    ? Component<SvgIcon, SvgIconProps>(new SvgIconProps(iconSource, 15))
                    : TextBlock($"[{Props.Icon}]").FontSize(14));

        // Determine if we need flex layout
        bool hasIcon = renderedIcon != null;
        bool hasLabel = !string.IsNullOrEmpty(Props.Label);

        Element buttonContent = hasIcon && hasLabel
            ? HStack(4, renderedIcon!, TextBlock(Props.Label!))
            : hasIcon
                ? renderedIcon!
                : hasLabel
                    ? TextBlock(Props.Label!)
                    : TextBlock("");

        var button = Button(buttonContent, Props.OnClick ?? (() => { }))
            .IsEnabled(!Props.Disabled);

        // Apply variant styling
        button = Props.Variant switch
        {
            "primary" => button.AccentButton(),
            "plain" => button,
            _ => button.SubtleButton()
        };

        // Add properties
        if (!string.IsNullOrEmpty(Props.Title))
        {
            button = button.Set(b => ToolTipService.SetToolTip(b, new ToolTip { Content = Props.Title }));
        }

        if (!string.IsNullOrEmpty(Props.AutomationName))
        {
            button = button.AutomationName(Props.AutomationName);
        }

        return button;
    }
}
