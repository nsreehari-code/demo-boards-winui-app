using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>BoardSwitcher.jsx</c> — board picker shown in the settings modal header.
/// Renders a select of available boards plus a "Switch" button that activates the chosen board.
/// Also supports layout toggle, theme toggle, and test runner buttons.
/// Props match frontend exactly: value, options ([{id, label}]), currentBoardId, onChange, onSwitch, etc.
/// </summary>
public sealed record BoardSwitcherProps(
    string Value = "",
    IReadOnlyList<object?>? Options = null,
    string CurrentBoardId = "",
    Action<string>? OnChange = null,
    Action? OnSwitch = null,
    bool SelectDisabled = false,
    bool Loading = false,
    string Error = "",
    string? LayoutKind = null,
    Action? OnToggleLayout = null,
    bool LayoutToggleDisabled = false,
    bool TogglingLayout = false,
    string? ThemePackId = null,
    Action? OnToggleTheme = null,
    bool ThemeToggleDisabled = false,
    bool TogglingTheme = false,
    bool SmokeRunnerEnabled = false,
    Action? OnRunSmokeRunner = null,
    string SmokeRunnerTitle = "",
    bool SmokeStrategistEnabled = false,
    Action? OnRunStrategist = null,
    string SmokeStrategistTitle = "");

public sealed class BoardSwitcher : Component<BoardSwitcherProps>
{
    private static string GetOptionId(object? opt) =>
        opt is IDictionary<string, object?> dict && dict.TryGetValue("id", out var id)
            ? id?.ToString() ?? ""
            : "";

    private static string GetOptionLabel(object? opt) =>
        opt is IDictionary<string, object?> dict && dict.TryGetValue("label", out var label)
            ? label?.ToString() ?? ""
            : "";

    public override Element Render()
    {
        var options = Props.Options ?? Array.Empty<object?>();
        bool showLayoutToggle = Props.OnToggleLayout != null;
        bool showThemeToggle = Props.OnToggleTheme != null;
        bool isCardsLayout = Props.LayoutKind == "flowing-cards";
        bool isSignalRoom = Props.ThemePackId == "signal-room";
        bool showRunButtons = Props.SmokeRunnerEnabled || Props.SmokeStrategistEnabled;

        // Build select options with "(current)" suffix for current board
        var selectOptions = new List<object?>();
        foreach (var option in options)
        {
            string optId = GetOptionId(option);
            string optLabel = GetOptionLabel(option);
            if (optId == Props.CurrentBoardId)
            {
                optLabel += " (current)";
            }
            selectOptions.Add(new { value = optId, label = optLabel });
        }

        var buttonElements = new List<Element>();

        // Switch button (forward-arrow icon node, plus "Switch" label when run buttons are hidden)
        Element forwardIcon = Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.ForwardArrow, 14));
        buttonElements.Add(
            Button(showRunButtons ? forwardIcon : HStack(6, forwardIcon, TextBlock("Switch")), Props.OnSwitch)
                .IsEnabled(Props.Value != Props.CurrentBoardId && Props.Value.Length > 0)
                .AutomationName("Switch to selected board")
                .AccentButton());

        // Smoke runner button (bi-flask)
        if (Props.SmokeRunnerEnabled)
        {
            buttonElements.Add(
                Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.Flask, 15)), Props.OnRunSmokeRunner)
                    .AutomationName(Props.SmokeRunnerTitle ?? "Run tests")
                    .SubtleButton());
        }

        // Smoke strategist button (bi-compass)
        if (Props.SmokeStrategistEnabled)
        {
            buttonElements.Add(
                Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.Compass, 15)), Props.OnRunStrategist)
                    .AutomationName(Props.SmokeStrategistTitle ?? "Run strategist")
                    .SubtleButton());
        }

        // Layout toggle button (bi-diagram-3 / bi-bounding-box)
        if (showLayoutToggle)
        {
            buttonElements.Add(
                Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(isCardsLayout ? HostIconSources.Diagram3 : HostIconSources.BoundingBox, 15)), Props.OnToggleLayout)
                    .IsEnabled(!(Props.LayoutToggleDisabled || Props.LayoutKind == null || Props.TogglingLayout))
                    .AutomationName(isCardsLayout ? "Switch to infinite canvas layout" : "Switch to flowing cards layout")
                    .SubtleButton());
        }

        // Theme toggle button (bi-brightness-high / bi-moon-stars)
        if (showThemeToggle)
        {
            buttonElements.Add(
                Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(isSignalRoom ? HostIconSources.BrightnessHigh : HostIconSources.MoonStars, 15)), Props.OnToggleTheme)
                    .IsEnabled(!(Props.ThemeToggleDisabled || Props.ThemePackId == null || Props.TogglingTheme))
                    .AutomationName(isSignalRoom ? "Switch to mist-ops theme" : "Switch to signal-room theme")
                    .SubtleButton());
        }

        return VStack(8,
            TextBlock("Board")
                .FontSize(12)
                .Opacity(0.7),
            HStack(8,
                Component<SelectControl, SelectControlProps>(new SelectControlProps(
                    Value: Props.Value,
                    Options: selectOptions.Cast<object?>().ToList(),
                    Disabled: Props.SelectDisabled,
                    EmptyLabel: Props.Loading ? "Loading boards…" : "No boards available",
                    AllowEmpty: options.Count == 0,
                    AriaLabel: "Select a board",
                    OnChange: Props.OnChange
                )).Flex(grow: 1),
                HStack(4, buttonElements.ToArray()).Flex(shrink: 0)
            ),
            string.IsNullOrWhiteSpace(Props.Error)
                ? TextBlock(string.Empty).Set(text => text.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed)
                : TextBlock(Props.Error)
                    .Opacity(0.78)
                    .Set(text => text.TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords)
        ).Flex(grow: 1);
    }
}
