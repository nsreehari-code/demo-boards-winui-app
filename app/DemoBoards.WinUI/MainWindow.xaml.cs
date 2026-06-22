using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DemoBoards_WinUI;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TitleBar AppTitleBar;
    private readonly Frame RootFrame;

    public Frame RootFrameControl => RootFrame;

    public MainWindow()
    {
        SystemBackdrop = new MicaBackdrop();

        AppTitleBar = new TitleBar { Title = "DemoBoards.WinUI" };
        AppTitleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")) };
        RootFrame = new Frame();

        var root = new Grid { Background = ResolveBrush("BoardWindowBackgroundBrush") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(AppTitleBar);
        Grid.SetRow(RootFrame, 1);
        root.Children.Add(RootFrame);
        Content = root;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Frame.Navigate(typeof(MainPage)) crashes natively for this pure-code
        // Page (no MainPage.xaml) under WinAppSDK 2.2.0, so assign the page
        // instance directly as the frame content instead.
        RootFrame.Content = new MainPage();
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }
}
