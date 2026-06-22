using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls;

public enum FloatingButtonPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed partial class FloatingCircleButton : UserControl
{
    private const string DefaultGlyph = "\uE713";

    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph),
        typeof(string),
        typeof(FloatingCircleButton),
        new PropertyMetadata(DefaultGlyph, OnVisualPropertyChanged));

    public static readonly DependencyProperty ToolTipTextProperty = DependencyProperty.Register(
        nameof(ToolTipText),
        typeof(string),
        typeof(FloatingCircleButton),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty SvgIconPathProperty = DependencyProperty.Register(
        nameof(SvgIconPath),
        typeof(string),
        typeof(FloatingCircleButton),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty FloatPositionProperty = DependencyProperty.Register(
        nameof(FloatPosition),
        typeof(FloatingButtonPosition),
        typeof(FloatingCircleButton),
        new PropertyMetadata(FloatingButtonPosition.BottomRight, OnVisualPropertyChanged));

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(42d, OnVisualPropertyChanged));

    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(16d, OnVisualPropertyChanged));

    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.Register(
        nameof(OffsetX),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(
        nameof(OffsetY),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty ButtonStyleProperty = DependencyProperty.Register(
        nameof(ButtonStyle),
        typeof(Style),
        typeof(FloatingCircleButton),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public FloatingCircleButton()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public event RoutedEventHandler? Click;

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string ToolTipText
    {
        get => (string)GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    public string SvgIconPath
    {
        get => (string)GetValue(SvgIconPathProperty);
        set => SetValue(SvgIconPathProperty, value);
    }

    public FloatingButtonPosition FloatPosition
    {
        get => (FloatingButtonPosition)GetValue(FloatPositionProperty);
        set => SetValue(FloatPositionProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    public double OffsetX
    {
        get => (double)GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    public double OffsetY
    {
        get => (double)GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    public Style? ButtonStyle
    {
        get => (Style?)GetValue(ButtonStyleProperty);
        set => SetValue(ButtonStyleProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FloatingCircleButton)d).UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        double effectiveDiameter = Diameter > 0 ? Diameter : 42d;
        Style? style = ResolveButtonStyle();

        RootButton.Style = style;
        RootButton.Width = effectiveDiameter;
        RootButton.Height = effectiveDiameter;
        RootButton.CornerRadius = new CornerRadius(effectiveDiameter / 2d);
        UpdateIconState(effectiveDiameter);
        ToolTipService.SetToolTip(RootButton, string.IsNullOrWhiteSpace(ToolTipText) ? null : ToolTipText);

        Width = effectiveDiameter;
        Height = effectiveDiameter;
        (HorizontalAlignment, VerticalAlignment, Thickness margin) = ResolvePlacement();
        Margin = margin;
    }

    private void UpdateIconState(double effectiveDiameter)
    {
        string svgIconPath = SvgIconPath?.Trim() ?? string.Empty;
        if (svgIconPath.Length > 0)
        {
            SvgIconView.Source = HostIconSources.CreateSvg(svgIconPath);
            SvgIconView.Width = System.Math.Max(16d, effectiveDiameter * 0.42d);
            SvgIconView.Height = SvgIconView.Width;
            SvgIconView.Visibility = Visibility.Visible;
            GlyphIconView.Visibility = Visibility.Collapsed;
            return;
        }

        GlyphIconView.Glyph = string.IsNullOrWhiteSpace(IconGlyph) ? DefaultGlyph : IconGlyph;
        GlyphIconView.FontSize = System.Math.Max(14d, effectiveDiameter * 0.38d);
        GlyphIconView.Visibility = Visibility.Visible;
        SvgIconView.Visibility = Visibility.Collapsed;
    }

    private (HorizontalAlignment Horizontal, VerticalAlignment Vertical, Thickness Margin) ResolvePlacement()
    {
        double inset = Inset;
        double offsetX = OffsetX;
        double offsetY = OffsetY;

        return FloatPosition switch
        {
            FloatingButtonPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(inset + offsetX, inset + offsetY, 0, 0)),
            FloatingButtonPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, inset + offsetY, inset - offsetX, 0)),
            FloatingButtonPosition.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(inset + offsetX, 0, 0, inset - offsetY)),
            _ => (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, inset - offsetX, inset - offsetY)),
        };
    }

    private Style? ResolveButtonStyle()
    {
        if (ButtonStyle is not null)
        {
            return ButtonStyle;
        }

        return Application.Current.Resources.TryGetValue("BoardFloatingCircleButtonStyle", out object resource)
            ? resource as Style
            : null;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}