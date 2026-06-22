using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI;

public sealed class FrePage : Page
{
    public FrePage()
    {
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var enterBoardButton = new Button
        {
            Content = "Enter the Board",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(20, 10, 20, 10),
        };
        enterBoardButton.Click += OnEnterBoardClick;

        return new Grid
        {
            Background = ResolveBrush("BoardWindowBackgroundBrush"),
            Children =
            {
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(28),
                    CornerRadius = new CornerRadius(20),
                    Background = ResolveBrush("CardBackgroundFillColorSecondaryBrush"),
                    Child = new StackPanel
                    {
                        Spacing = 18,
                        Width = 360,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "DemoBoards",
                                FontSize = 32,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Foreground = ResolveBrush("BoardTextBrush"),
                            },
                            new TextBlock
                            {
                                Text = "Launch the board canvas only when you are ready to enter the runtime surface.",
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.WrapWholeWords,
                                Foreground = ResolveBrush("BoardTextMutedBrush"),
                            },
                            enterBoardButton,
                        }
                    }
                }
            }
        };
    }

    private void OnEnterBoardClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.RootFrameControl.Navigate(typeof(MainPage));
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }
}