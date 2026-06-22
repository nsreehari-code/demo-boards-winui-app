using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class GlobalModal : UserControl
{
    private readonly Grid Backdrop;
    private readonly Border DialogBorder;
    private readonly TextBlock TitleText;
    private readonly Button CloseButton;
    private readonly ContentPresenter BodyHost;

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

        var headerGrid = new Grid();
        headerGrid.Children.Add(TitleText);
        headerGrid.Children.Add(CloseButton);

        var dialogGrid = new Grid { RowSpacing = 14 };
        dialogGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialogGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        dialogGrid.Children.Add(headerGrid);
        Grid.SetRow(BodyHost, 1);
        dialogGrid.Children.Add(BodyHost);

        DialogBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 1180,
            MinWidth = 760,
            MaxHeight = 820,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(18),
            Background = ResolveBrush("LayerOnAcrylicFillColorDefaultBrush"),
            BorderBrush = ResolveBrush("BoardBorderStrongBrush"),
            BorderThickness = new Thickness(1),
            Child = dialogGrid,
        };

        Backdrop = new Grid
        {
            Background = ResolveBrush("BoardOverlayBrush"),
        };
        Backdrop.Tapped += OnBackdropTapped;
        Backdrop.Children.Add(DialogBorder);
        Content = Backdrop;
    }

    public event EventHandler? CloseRequested;

    public void Show(string title, UIElement content)
    {
        TitleText.Text = title;
        BodyHost.Content = content;
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        BodyHost.Content = null;
        Visibility = Visibility.Collapsed;
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
