using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public enum PanelRailSide
{
    Left,
    Right,
}

public sealed record PanelRailVisualStyle(
    Brush? PanelBackground = null,
    Brush? PanelBorderBrush = null,
    Brush? OverlayBrush = null,
    Thickness? PanelBorderThickness = null,
    Thickness? PanelPadding = null,
    Thickness? PanelMargin = null,
    Thickness? ContentMargin = null,
    CornerRadius? PanelCornerRadius = null);

public sealed record PanelRailOptions(
    PanelRailSide Side,
    FloatingButtonPosition ButtonPosition,
    double PanelWidth,
    string OpenToolTipText,
    string CloseToolTipText,
    string OpenGlyph = "\uE713",
    string CloseGlyph = "\uE711",
    string OpenSvgIconPath = "",
    double ButtonInset = 16,
    double ButtonOffsetX = 0,
    double ButtonOffsetY = 0,
    double ButtonDiameter = 48,
    double ButtonRailGap = 10,
    bool WrapContentInScrollViewer = true,
    Style? ButtonStyle = null,
    Style? ActiveButtonStyle = null,
    PanelRailVisualStyle? VisualStyle = null);

public sealed class PanelRail : UserControl
{
    private readonly Grid railHost;
    private readonly Grid backdrop;
    private readonly Border panelBorder;
    private readonly ContentPresenter contentHost;
    private readonly ScrollViewer scrollHost;
    private readonly FloatingCircleButton toggleButton;

    private PanelRailOptions options = new(
        PanelRailSide.Right,
        FloatingButtonPosition.TopRight,
        420,
        "Open panel",
        "Close panel");

    public PanelRail()
    {
        contentHost = new ContentPresenter();
        scrollHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = contentHost,
        };

        panelBorder = new Border
        {
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        backdrop = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = ResolveBrush("BoardOverlayBrush"),
            IsHitTestVisible = false,
        };

        railHost = new Grid
        {
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        railHost.Children.Add(backdrop);
        railHost.Children.Add(panelBorder);

        toggleButton = new FloatingCircleButton();
        toggleButton.Click += OnToggleButtonClick;

        var root = new Grid();
        root.Children.Add(railHost);
        root.Children.Add(toggleButton);
        Content = root;

        ApplyOptions();
        SetOpen(false);
    }

    public event EventHandler? Opening;

    public bool IsOpen { get; private set; }

    public Visibility ToggleVisibility
    {
        get => toggleButton.Visibility;
        set
        {
            toggleButton.Visibility = value;
            if (value != Visibility.Visible)
            {
                SetOpen(false);
            }
        }
    }

    public void Configure(PanelRailOptions nextOptions)
    {
        options = nextOptions;
        ApplyOptions();
    }

    public void SetPanelContent(UIElement content)
    {
        contentHost.Content = content;
    }

    public void SetOpen(bool open)
    {
        IsOpen = open;
        railHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        backdrop.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        panelBorder.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleVisualState();
    }

    public void Toggle()
    {
        if (!IsOpen)
        {
            Opening?.Invoke(this, EventArgs.Empty);
        }

        SetOpen(!IsOpen);
    }

    private void ApplyOptions()
    {
        toggleButton.FloatPosition = options.ButtonPosition;
        toggleButton.Inset = options.ButtonInset;
        toggleButton.OffsetX = options.ButtonOffsetX;
        toggleButton.OffsetY = options.ButtonOffsetY;
        toggleButton.Diameter = options.ButtonDiameter;
        toggleButton.ButtonStyle = options.ButtonStyle;
        toggleButton.ActiveButtonStyle = options.ActiveButtonStyle;

        backdrop.Background = options.VisualStyle?.OverlayBrush ?? ResolveBrush("BoardOverlayBrush");

        double buttonLaneWidth = ResolveButtonLaneWidth();
        railHost.Width = options.PanelWidth + buttonLaneWidth;
        railHost.HorizontalAlignment = options.Side == PanelRailSide.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        panelBorder.HorizontalAlignment = options.Side == PanelRailSide.Right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        panelBorder.Width = options.PanelWidth;
        panelBorder.Padding = options.VisualStyle?.PanelPadding ?? new Thickness(0);
        panelBorder.Margin = ResolvePanelMargin(buttonLaneWidth, options.VisualStyle?.PanelMargin ?? new Thickness(0));
        panelBorder.Background = options.VisualStyle?.PanelBackground ?? ResolveBrush("CardBackgroundFillColorDefaultBrush");
        panelBorder.BorderBrush = options.VisualStyle?.PanelBorderBrush ?? ResolveBrush("BoardBorderStrongBrush");
        panelBorder.BorderThickness = options.VisualStyle?.PanelBorderThickness ?? new Thickness(1);
        panelBorder.CornerRadius = options.VisualStyle?.PanelCornerRadius ?? ResolveDefaultCornerRadius(options.Side);

        contentHost.Margin = options.VisualStyle?.ContentMargin ?? new Thickness(0);
        panelBorder.Child = options.WrapContentInScrollViewer ? scrollHost : contentHost;
        UpdateToggleVisualState();
    }

    private void UpdateToggleVisualState()
    {
        toggleButton.IsActive = IsOpen;
        toggleButton.ToolTipText = IsOpen ? options.CloseToolTipText : options.OpenToolTipText;
        if (IsOpen)
        {
            toggleButton.SvgIconPath = string.Empty;
            toggleButton.IconGlyph = options.CloseGlyph;
            return;
        }

        toggleButton.SvgIconPath = options.OpenSvgIconPath;
        toggleButton.IconGlyph = string.IsNullOrWhiteSpace(options.OpenSvgIconPath) ? options.OpenGlyph : string.Empty;
    }

    private void OnToggleButtonClick(object sender, RoutedEventArgs e)
    {
        Toggle();
    }

    private double ResolveButtonLaneWidth()
    {
        double inset = Math.Max(0d, options.ButtonInset);
        double diameter = Math.Max(0d, options.ButtonDiameter);
        double offsetMagnitude = Math.Abs(options.ButtonOffsetX);
        double gap = Math.Max(0d, options.ButtonRailGap);
        return inset + diameter + offsetMagnitude + gap;
    }

    private Thickness ResolvePanelMargin(double buttonLaneWidth, Thickness baseMargin)
    {
        return options.Side == PanelRailSide.Right
            ? new Thickness(baseMargin.Left, baseMargin.Top, baseMargin.Right + buttonLaneWidth, baseMargin.Bottom)
            : new Thickness(baseMargin.Left + buttonLaneWidth, baseMargin.Top, baseMargin.Right, baseMargin.Bottom);
    }

    private static CornerRadius ResolveDefaultCornerRadius(PanelRailSide side)
    {
        return side == PanelRailSide.Right
            ? new CornerRadius(24, 0, 0, 24)
            : new CornerRadius(0, 24, 24, 0);
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }
}
