using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI;

public sealed partial class FrePage : Page
{
    public FrePage()
    {
        InitializeComponent();
    }

    private void OnEnterBoardClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.RootFrameControl.Navigate(typeof(MainPage));
    }
}