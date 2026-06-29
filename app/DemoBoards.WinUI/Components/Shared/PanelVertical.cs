using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using Windows.UI;

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
    private const double LayerInset = 12;
    private const double FabSlotWidth = 52;

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        (VerticalAlignment verticalAlignment, HorizontalAlignment horizontalAlignment, bool isRight) = ResolvePlacement(Props.FabPosition);

        // Mirrors PanelVertical.jsx: `title` is the FAB tooltip only — it never renders as a
        // visible panel header. The expanded panel shows `children` verbatim.
        Element fab = Component<FloatingCircularButton, FloatingCircularButtonProps>(
            new FloatingCircularButtonProps(
                Toggled: Props.Expanded,
                Icon: Props.Icon,
                IconToggled: Props.IconToggled,
                OnClick: () => Props.OnToggle?.Invoke(),
                OnClickToggled: () => Props.OnToggle?.Invoke(),
                AriaLabel: Props.Title ?? Props.AriaLabel ?? "Toggle panel"));

        Element fabHost = fab
            .HAlign(horizontalAlignment)
            .VAlign(verticalAlignment);

        (double fabLeft, double fabTop, double fabRight, double fabBottom) = ResolveFabMargin(verticalAlignment, isRight);

        if (!Props.Expanded)
        {
            return Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                fabHost.Margin(fabLeft, fabTop, fabRight, fabBottom));
        }

        (double panelLeft, double panelTop, double panelRight, double panelBottom) = ResolvePanelMargin(isRight);

        Element panel = Border(
                ScrollViewer(Props.Children ?? Empty())
                    .Flex(grow: 1)
                    .Set(scrollViewer =>
                    {
                        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                    }))
            .Padding(12)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(0)
            .Width(480)
            .MaxWidth(560)
            .HAlign(horizontalAlignment)
            .VAlign(VerticalAlignment.Stretch)
            .Margin(panelLeft, panelTop, panelRight, panelBottom)
            .AutomationName(Props.AriaLabel ?? Props.Title ?? "Panel");

        return Grid(
            new[] { GridSize.Star() },
            new[] { GridSize.Star() },
            Border(Empty()).Background(theme.Overlay),
            panel,
            fabHost.Margin(fabLeft, fabTop, fabRight, fabBottom));
    }

    private static (double Left, double Top, double Right, double Bottom) ResolveFabMargin(VerticalAlignment verticalAlignment, bool isRight)
    {
        double top = verticalAlignment == VerticalAlignment.Top ? LayerInset : 0;
        double bottom = verticalAlignment == VerticalAlignment.Bottom ? LayerInset : 0;
        double left = isRight ? 0 : LayerInset;
        double right = isRight ? LayerInset : 0;
        return (left, top, right, bottom);
    }

    private static (double Left, double Top, double Right, double Bottom) ResolvePanelMargin(bool isRight)
    {
        return isRight
            ? (LayerInset, 0, LayerInset + FabSlotWidth, 0)
            : (LayerInset + FabSlotWidth, 0, LayerInset, 0);
    }

    private static (VerticalAlignment Vertical, HorizontalAlignment Horizontal, bool IsRight) ResolvePlacement(string? fabPosition)
    {
        string[] parts = (fabPosition ?? "top-left").Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        bool isBottom = parts.Length > 0 && string.Equals(parts[0], "bottom", StringComparison.OrdinalIgnoreCase);
        bool isRight = parts.Length > 1 && string.Equals(parts[1], "right", StringComparison.OrdinalIgnoreCase);
        return (
            isBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            isRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            isRight);
    }
}
