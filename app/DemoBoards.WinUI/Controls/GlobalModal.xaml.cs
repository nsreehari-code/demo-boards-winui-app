using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed partial class GlobalModal : UserControl
{
    public GlobalModal()
    {
        InitializeComponent();
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
}
