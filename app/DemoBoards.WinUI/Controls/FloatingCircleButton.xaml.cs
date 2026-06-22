using DemoBoards_WinUI.Assets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DemoBoards_WinUI.Controls;

public enum FloatingButtonPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed class FloatingCircleButton : UserControl
{
        private readonly Button RootButton;
        private readonly Image SvgIconView;
        private readonly FontIcon GlyphIconView;

    private const string DefaultGlyph = "\uE713";
    private const double RestingOpacity = 0.76d;
    private const double HoverOpacity = 0.94d;

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

    public static readonly DependencyProperty ActiveButtonStyleProperty = DependencyProperty.Register(
        nameof(ActiveButtonStyle),
        typeof(Style),
        typeof(FloatingCircleButton),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(FloatingCircleButton),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty BaseOpacityProperty = DependencyProperty.Register(
        nameof(BaseOpacity),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(RestingOpacity, OnVisualPropertyChanged));

    public static readonly DependencyProperty HoverButtonOpacityProperty = DependencyProperty.Register(
        nameof(HoverButtonOpacity),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(HoverOpacity, OnVisualPropertyChanged));

    public static readonly DependencyProperty ActiveButtonOpacityProperty = DependencyProperty.Register(
        nameof(ActiveButtonOpacity),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(HoverOpacity, OnVisualPropertyChanged));

    public static readonly DependencyProperty DisabledButtonOpacityProperty = DependencyProperty.Register(
        nameof(DisabledButtonOpacity),
        typeof(double),
        typeof(FloatingCircleButton),
        new PropertyMetadata(0.48d, OnVisualPropertyChanged));

    public FloatingCircleButton()
    {
        SvgIconView = new Image
        {
            Width = 18,
            Height = 18,
            Visibility = Visibility.Collapsed,
        };

        GlyphIconView = new FontIcon();

        RootButton = new Button
        {
            Content = new Grid
            {
                Children =
                {
                    SvgIconView,
                    GlyphIconView,
                }
            }
        };

        Content = RootButton;

        RootButton.Click += OnButtonClick;
        RootButton.PointerEntered += OnButtonPointerEntered;
        RootButton.PointerExited += OnButtonPointerExited;
        RootButton.GotFocus += OnButtonGotFocus;
        RootButton.LostFocus += OnButtonLostFocus;
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

    public Style? ActiveButtonStyle
    {
        get => (Style?)GetValue(ActiveButtonStyleProperty);
        set => SetValue(ActiveButtonStyleProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double BaseOpacity
    {
        get => (double)GetValue(BaseOpacityProperty);
        set => SetValue(BaseOpacityProperty, value);
    }

    public double HoverButtonOpacity
    {
        get => (double)GetValue(HoverButtonOpacityProperty);
        set => SetValue(HoverButtonOpacityProperty, value);
    }

    public double ActiveButtonOpacity
    {
        get => (double)GetValue(ActiveButtonOpacityProperty);
        set => SetValue(ActiveButtonOpacityProperty, value);
    }

    public double DisabledButtonOpacity
    {
        get => (double)GetValue(DisabledButtonOpacityProperty);
        set => SetValue(DisabledButtonOpacityProperty, value);
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
        RootButton.Opacity = ResolveCurrentOpacity();
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
        if (IsActive && ActiveButtonStyle is not null)
        {
            return ActiveButtonStyle;
        }

        if (IsActive
            && Application.Current.Resources.TryGetValue("BoardFloatingCircleButtonActiveStyle", out object activeResource)
            && activeResource is Style activeStyle)
        {
            return activeStyle;
        }

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

    private void OnButtonPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        RootButton.Opacity = NormalizeOpacity(HoverButtonOpacity, HoverOpacity);
    }

    private void OnButtonPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        RootButton.Opacity = RootButton.FocusState == FocusState.Unfocused
            ? ResolveCurrentOpacity()
            : NormalizeOpacity(HoverButtonOpacity, HoverOpacity);
    }

    private void OnButtonGotFocus(object sender, RoutedEventArgs e)
    {
        RootButton.Opacity = NormalizeOpacity(HoverButtonOpacity, HoverOpacity);
    }

    private void OnButtonLostFocus(object sender, RoutedEventArgs e)
    {
        RootButton.Opacity = ResolveCurrentOpacity();
    }

    private double ResolveCurrentOpacity()
    {
        if (!IsEnabled || !RootButton.IsEnabled)
        {
            return NormalizeOpacity(DisabledButtonOpacity, 0.48d);
        }

        return IsActive
            ? NormalizeOpacity(ActiveButtonOpacity, HoverOpacity)
            : NormalizeOpacity(BaseOpacity, RestingOpacity);
    }

    private static double NormalizeOpacity(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return System.Math.Max(0d, System.Math.Min(1d, value));
    }
}