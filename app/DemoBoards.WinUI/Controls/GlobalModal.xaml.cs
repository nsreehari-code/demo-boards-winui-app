using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class GlobalModal : UserControl
{
    public enum ModalPresentation
    {
        Dialog,
        SidePanel,
    }

    private readonly Grid Backdrop;
    private readonly Border DialogBorder;
    private readonly TextBlock TitleText;
    private readonly Button CloseButton;
    private readonly ContentPresenter BodyHost;
    private readonly Grid DialogRoot;
    private readonly Grid HeaderGrid;
    private readonly Border HeaderBorder;
    private ModalPresentation currentPresentation = ModalPresentation.Dialog;

    public GlobalModal()
    {
        Visibility = Visibility.Collapsed;

        TitleText = new TextBlock
        {
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        CloseButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        CloseButton.Click += OnCloseClick;

        BodyHost = new ContentPresenter();

        HeaderGrid = new Grid();
        HeaderGrid.Children.Add(TitleText);
        HeaderGrid.Children.Add(CloseButton);

        HeaderBorder = new Border
        {
            Child = HeaderGrid,
        };

        DialogRoot = new Grid { RowSpacing = 14 };
        DialogRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DialogRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        DialogRoot.Children.Add(HeaderBorder);
        Grid.SetRow(BodyHost, 1);
        DialogRoot.Children.Add(BodyHost);

        DialogBorder = new Border
        {
            Child = DialogRoot,
        };

        Backdrop = new Grid
        {
            Background = ResolveBrush("BoardOverlayBrush"),
        };
        Backdrop.Tapped += OnBackdropTapped;
        Backdrop.Children.Add(DialogBorder);
        Content = Backdrop;

        ApplyPresentation(ModalPresentation.Dialog);
    }

    public event EventHandler? CloseRequested;

    public void Show(string title, UIElement content, ModalPresentation presentation = ModalPresentation.Dialog)
    {
        currentPresentation = presentation;
        ApplyPresentation(presentation);
        TitleText.Text = title;
        BodyHost.Content = content;
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        ApplyPresentation(ModalPresentation.Dialog);
        BodyHost.Content = null;
        Visibility = Visibility.Collapsed;
    }

    private void ApplyPresentation(ModalPresentation presentation)
    {
        bool isSidePanel = presentation == ModalPresentation.SidePanel;

        Backdrop.Padding = isSidePanel ? new Thickness(16, 16, 16, 80) : new Thickness(0);

        TitleText.FontSize = isSidePanel ? 14 : 22;

        CloseButton.Content = isSidePanel ? "X" : "Close";
        CloseButton.Width = isSidePanel ? 36 : double.NaN;
        CloseButton.Height = isSidePanel ? 36 : double.NaN;
        CloseButton.Padding = isSidePanel ? new Thickness(0) : new Thickness(12, 6, 12, 6);

        HeaderBorder.Padding = isSidePanel ? new Thickness(16, 14, 16, 14) : new Thickness(0);
        HeaderBorder.Background = isSidePanel
            ? CreateVerticalGradientBrush(Windows.UI.Color.FromArgb(0xFA, 0x3A, 0x4E, 0x67), Windows.UI.Color.FromArgb(0xF2, 0x28, 0x3A, 0x50))
            : null;
        HeaderBorder.BorderBrush = isSidePanel
            ? CreateSolidBrush(Windows.UI.Color.FromArgb(0x9E, 0x14, 0x22, 0x33))
            : null;
        HeaderBorder.BorderThickness = isSidePanel ? new Thickness(0, 0, 0, 1) : new Thickness(0);

        TitleText.Foreground = isSidePanel ? CreateSolidBrush(Windows.UI.Color.FromArgb(0xF5, 0xF6, 0xF9, 0xFC)) : null;
        CloseButton.HorizontalAlignment = HorizontalAlignment.Right;
        CloseButton.Foreground = isSidePanel ? CreateSolidBrush(Windows.UI.Color.FromArgb(0xF5, 0xF6, 0xF9, 0xFC)) : null;
        CloseButton.Background = isSidePanel ? CreateSolidBrush(Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)) : null;
        CloseButton.BorderBrush = isSidePanel ? CreateSolidBrush(Windows.UI.Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)) : null;
        CloseButton.BorderThickness = isSidePanel ? new Thickness(1) : new Thickness(0);
        CloseButton.CornerRadius = isSidePanel ? new CornerRadius(999) : new CornerRadius(8);

        DialogRoot.RowSpacing = isSidePanel ? 0 : 14;

        DialogBorder.HorizontalAlignment = isSidePanel ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        DialogBorder.VerticalAlignment = isSidePanel ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        DialogBorder.Width = isSidePanel ? 448 : double.NaN;
        DialogBorder.MaxWidth = isSidePanel ? 448 : 1180;
        DialogBorder.MinWidth = isSidePanel ? 420 : 760;
        DialogBorder.MaxHeight = isSidePanel ? double.PositiveInfinity : 820;
        DialogBorder.Padding = isSidePanel ? new Thickness(0) : new Thickness(18);
        DialogBorder.Margin = isSidePanel ? new Thickness(0, 0, 0, 0) : new Thickness(0);
        DialogBorder.CornerRadius = isSidePanel ? new CornerRadius(24, 24, 0, 0) : new CornerRadius(18);
        DialogBorder.Background = isSidePanel
            ? CreateVerticalGradientBrush(Windows.UI.Color.FromArgb(0xFD, 0xE2, 0xEA, 0xF1), Windows.UI.Color.FromArgb(0xFD, 0xD6, 0xDF, 0xE8))
            : ResolveBrush("LayerOnAcrylicFillColorDefaultBrush");
        DialogBorder.BorderBrush = isSidePanel
            ? CreateSolidBrush(Windows.UI.Color.FromArgb(0x3D, 0x40, 0x60, 0x83))
            : ResolveBrush("BoardBorderStrongBrush");
        DialogBorder.BorderThickness = new Thickness(1);
    }

    private static Brush CreateSolidBrush(Windows.UI.Color color)
    {
        return new SolidColorBrush(color);
    }

    private static Brush CreateVerticalGradientBrush(Windows.UI.Color start, Windows.UI.Color end)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        return brush;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackdropTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }
}
