using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.State;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DemoBoards_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private static readonly string StartupLogPath = Path.Combine(Path.GetTempPath(), "DemoBoards.WinUI.startup.log");
    private Window? _window;
    private ResourceDictionary? currentThemeDictionary;
    private DemoBoardsRuntimeService? runtimeService;
    private BoardStore? boardStore;
    private EmbeddedBoardClient? boardClient;
    private WinUiAppConfig? appConfig;
    private WinUiHostConfigService? hostConfigService;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        LogStartup("App constructor initialized.");
        LogStartup("Default theme pack provided by App.xaml merged dictionaries.");

        UnhandledException += OnUnhandledException;
    }

    public DemoBoardsRuntimeService RuntimeService => runtimeService ?? throw new InvalidOperationException("Runtime service not initialized.");
    public BoardStore BoardStore => boardStore ?? throw new InvalidOperationException("Board store not initialized.");
    public EmbeddedBoardClient BoardClient => boardClient ?? throw new InvalidOperationException("Board client not initialized.");
    public Window MainWindow => _window ?? throw new InvalidOperationException("Main window not initialized.");
    public WinUiAppConfig HostConfig => appConfig ?? throw new InvalidOperationException("App config not initialized.");
    public WinUiHostConfigService HostConfigService => hostConfigService ?? throw new InvalidOperationException("Host config service not initialized.");
    public string CurrentThemePackId { get; private set; } = BoardTheme.DefaultThemePackId;

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        LogStartup("OnLaunched entered.");

        try
        {
            appConfig = WinUiAppConfigLoader.Load(AppContext.BaseDirectory);
            Controls.BoardCanvasLayoutEngine.ConfigureDefaults(appConfig.Frontend.CanvasLayout);
            hostConfigService = new WinUiHostConfigService(appConfig, AppContext.BaseDirectory);
            LogStartup($"App config loaded. Templates config path: {appConfig.Backend.TemplatesConfigPath}");
            runtimeService = new DemoBoardsRuntimeService();
            LogStartup("Runtime service created.");
            await runtimeService.StartAsync();
            LogStartup("Runtime service started.");
            boardStore = new BoardStore(runtimeService);
            boardClient = new EmbeddedBoardClient(runtimeService);
            _window = new MainWindow();
            LogStartup("MainWindow constructed.");
            _window.Activate();
            LogStartup("MainWindow activated.");
        }
        catch (Exception ex)
        {
            LogStartup($"Startup failed.{Environment.NewLine}{ex}");
            ShowStartupFailureWindow(ex);
        }
    }

    public void ApplyThemePack(string? themePackId)
    {
        string normalizedTheme = BoardTheme.NormalizeThemePackId(themePackId);
        if (CurrentThemePackId == normalizedTheme && currentThemeDictionary is not null)
        {
            return;
        }

        var dictionary = new ResourceDictionary
        {
            Source = BoardTheme.GetThemeDictionaryUri(normalizedTheme)
        };

        if (currentThemeDictionary is null)
        {
            foreach (ResourceDictionary existingDictionary in Resources.MergedDictionaries
                         .Where(IsThemeDictionary)
                         .ToList())
            {
                Resources.MergedDictionaries.Remove(existingDictionary);
            }
        }

        if (currentThemeDictionary is not null)
        {
            Resources.MergedDictionaries.Remove(currentThemeDictionary);
        }

        Resources.MergedDictionaries.Add(dictionary);
        currentThemeDictionary = dictionary;
        CurrentThemePackId = normalizedTheme;
    }

    private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogStartup($"Unhandled exception.{Environment.NewLine}{e.Exception}");
        boardStore?.Dispose();
        if (runtimeService is not null)
        {
            await runtimeService.DisposeAsync();
        }
    }

    private void ShowStartupFailureWindow(Exception ex)
    {
        var fallbackWindow = new Window();
        fallbackWindow.Content = new ScrollViewer
        {
            Content = new TextBox
            {
                Text = $"DemoBoards.WinUI failed to start.{Environment.NewLine}{Environment.NewLine}{ex}",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Margin = new Thickness(16)
            }
        };

        _window = fallbackWindow;
        fallbackWindow.Activate();
    }

    private static void LogStartup(string message)
    {
        try
        {
            File.AppendAllText(
                StartupLogPath,
                $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        string? source = dictionary.Source?.OriginalString;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.EndsWith("Themes/MistOps.xaml", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith("Themes/SignalRoom.xaml", StringComparison.OrdinalIgnoreCase);
    }
}
