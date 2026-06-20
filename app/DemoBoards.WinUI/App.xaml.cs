using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using DemoBoards.RuntimeHost;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DemoBoards_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private DemoBoardsRuntimeService? runtimeService;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public DemoBoardsRuntimeService RuntimeService => runtimeService ?? throw new InvalidOperationException("Runtime service not initialized.");

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        runtimeService = new DemoBoardsRuntimeService();
        await runtimeService.StartAsync();
        _window = new MainWindow();
        _window.Activate();
    }

    private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (runtimeService is not null)
        {
            await runtimeService.DisposeAsync();
        }
    }
}
