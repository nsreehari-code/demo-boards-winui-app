using System;
using System.IO;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;
using Microsoft.UI.Xaml;

namespace DemoBoards_WinUI;

public sealed class App : IAsyncDisposable
{
    private static readonly string StartupLogPath = Path.Combine(Path.GetTempPath(), "DemoBoards.WinUI.startup.log");
    private Window? window;
    private ResourceDictionary? currentThemeDictionary;
    private DemoBoardsRuntimeService? runtimeService;
    private BoardStore? boardStore;
    private EmbeddedBoardClient? boardClient;
    private WinUiAppConfig? appConfig;
    private bool started;
    private bool uiResourcesInitialized;

    private App()
    {
    }

    public static App Current { get; } = new();

    public Exception? StartupException { get; private set; }
    public DemoBoardsRuntimeService RuntimeService => runtimeService ?? throw new InvalidOperationException("Runtime service not initialized.");
    public BoardStore BoardStore => boardStore ?? throw new InvalidOperationException("Board store not initialized.");
    public EmbeddedBoardClient BoardClient => boardClient ?? throw new InvalidOperationException("Board client not initialized.");
    public Window MainWindow => window ?? throw new InvalidOperationException("Main window not initialized.");
    public WinUiAppConfig HostConfig => appConfig ?? throw new InvalidOperationException("App config not initialized.");
    public string CurrentThemePackId { get; private set; } = BoardTheme.DefaultThemePackId;

    public void Start()
    {
        if (started || StartupException is not null)
        {
            return;
        }

        LogStartup("Reactor app startup entered.");

        try
        {
            appConfig = WinUiAppConfigLoader.Load(AppContext.BaseDirectory);
            BoardCanvasLayoutEngine.ConfigureDefaults(appConfig.Frontend.CanvasLayout);
            LogStartup($"App config loaded. Templates config path: {appConfig.Backend.TemplatesConfigPath}");
            Environment.SetEnvironmentVariable(RuntimeAssetResolver.NsCodeRepoRootEnvVar, appConfig.Backend.NsCodeRepoRoot);
            Environment.SetEnvironmentVariable(RuntimeAssetResolver.HostInvocationRunnerPathEnvVar, appConfig.Backend.HostInvocationRunnerPath);
            string initialBoardId = WinUiBoardIdStore.LoadOverride() ?? appConfig.Frontend.DefaultBoardId;
            GlobalStateStore.Current.Set(GlobalStateKeys.BoardId, initialBoardId);
            LogStartup($"Initial board id resolved: {initialBoardId}");
            runtimeService = new DemoBoardsRuntimeService(
                options: new RuntimeHostOptions(
                    appConfig.Backend.AgentfacePort,
                    appConfig.Backend.RequireFixedAgentfacePort,
                    initialBoardId));
            LogStartup("Runtime service created.");
            runtimeService.StartAsync().GetAwaiter().GetResult();
            LogStartup("Runtime service started.");
            boardStore = new BoardStore(runtimeService, appConfig.Frontend.BoardServerConstants);
            boardClient = new EmbeddedBoardClient(runtimeService);
            started = true;
        }
        catch (Exception ex)
        {
            StartupException = ex;
            LogStartup($"Startup failed.{Environment.NewLine}{ex}");
        }
    }

    public void AttachWindow(Window reactorWindow)
    {
        window = reactorWindow ?? throw new ArgumentNullException(nameof(reactorWindow));
    }

    public void EnsureUiResources()
    {
        if (uiResourcesInitialized)
        {
            return;
        }

        currentThemeDictionary = BoardTheme.CreateThemeDictionary(BoardTheme.DefaultThemePackId);
        Application.Current.Resources.MergedDictionaries.Add(currentThemeDictionary);
        uiResourcesInitialized = true;
        LogStartup("Theme resources initialized.");
    }

    public void ApplyThemePack(string? themePackId)
    {
        EnsureUiResources();

        string normalizedTheme = BoardTheme.NormalizeThemePackId(themePackId);
        if (CurrentThemePackId == normalizedTheme && currentThemeDictionary is not null)
        {
            return;
        }

        var dictionary = BoardTheme.CreateThemeDictionary(normalizedTheme);

        if (currentThemeDictionary is not null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(currentThemeDictionary);
        }

        Application.Current.Resources.MergedDictionaries.Add(dictionary);
        currentThemeDictionary = dictionary;
        CurrentThemePackId = normalizedTheme;
    }

    public async ValueTask DisposeAsync()
    {
        boardStore?.Dispose();
        if (runtimeService is not null)
        {
            await runtimeService.DisposeAsync();
        }
    }

    /// <summary>
    /// Relaunches the app so the embedded runtime re-initializes on the persisted board id —
    /// the WinUI analog of the frontend's <c>window.location.reload()</c> after a board switch.
    /// </summary>
    public void RequestRestart()
    {
        LogStartup("Board switch requested app relaunch.");
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
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
}