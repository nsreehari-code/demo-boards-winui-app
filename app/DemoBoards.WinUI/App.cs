using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.Lib;
using DemoBoards_WinUI.State;
using Microsoft.UI.Xaml;

namespace DemoBoards_WinUI;

public sealed record BoardSessionChangedEventArgs(string BoardId, string ServerUrl);

public sealed class App : IAsyncDisposable
{
    private static readonly string StartupLogPath = Path.Combine(Path.GetTempPath(), "DemoBoards.WinUI.startup.log");
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private Window? window;
    private ResourceDictionary? currentThemeDictionary;
    private HostedBoardStateService? boardStateService;
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
    public event EventHandler<BoardSessionChangedEventArgs>? SessionChanged;
    public HostedBoardStateService BoardStateService => boardStateService ?? throw new InvalidOperationException("Board state service not initialized.");
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
            GlobalStateStore globalState = GlobalStateStore.Current;
            string defaultBoardId = appConfig.Frontend.DefaultBoardId;
            string initialBoardId = globalState.GetOrAdd(GlobalStateKeys.BoardId, defaultBoardId);
            string defaultServerUrl = appConfig.Frontend.InitialServerUrl;
            string initialServerUrl = globalState.GetOrAdd(GlobalStateKeys.ServerUrl, defaultServerUrl);
            globalState.GetOrAdd(GlobalStateKeys.TestPageMode, false);
            globalState.Set(GlobalStateKeys.BoardId, initialBoardId);
            globalState.Set(GlobalStateKeys.ServerUrl, initialServerUrl);
            LogStartup($"Initial board id resolved: {initialBoardId}");
            LogStartup($"Initial server url resolved: {initialServerUrl}");

            try
            {
                (boardStateService, boardStore, boardClient) = CreateBoardSessionAsync(initialServerUrl, initialBoardId).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (
                !string.Equals(initialBoardId, defaultBoardId, StringComparison.Ordinal)
                || !string.Equals(NormalizeServerOrigin(initialServerUrl), NormalizeServerOrigin(defaultServerUrl), StringComparison.OrdinalIgnoreCase))
            {
                LogStartup($"Saved session override failed at startup: board={initialBoardId}, server={initialServerUrl}. Falling back to configured defaults board={defaultBoardId}, server={defaultServerUrl}.{Environment.NewLine}{ex}");
                globalState.Set(GlobalStateKeys.BoardId, defaultBoardId);
                globalState.Set(GlobalStateKeys.ServerUrl, defaultServerUrl);
                (boardStateService, boardStore, boardClient) = CreateBoardSessionAsync(defaultServerUrl, defaultBoardId).GetAwaiter().GetResult();
            }

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
        if (boardStateService is not null)
        {
            await boardStateService.DisposeAsync();
        }

        sessionGate.Dispose();
    }

    public async Task RebindBoardSessionAsync(string serverUrl, string boardId)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        string normalizedServerUrl = NormalizeServerUrl(serverUrl);

        await sessionGate.WaitAsync().ConfigureAwait(false);

        HostedBoardStateService? previousBoardStateService = null;
        BoardStore? previousBoardStore = null;

        try
        {
            if (appConfig is null)
            {
                throw new InvalidOperationException("App config not initialized.");
            }

            if (boardStateService is not null
                && string.Equals(boardStateService.BoardId, normalizedBoardId, StringComparison.Ordinal)
                && string.Equals(NormalizeServerOrigin(boardStateService.ServerBaseUri.AbsoluteUri), NormalizeServerOrigin(normalizedServerUrl), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            (HostedBoardStateService nextBoardStateService, BoardStore nextBoardStore, EmbeddedBoardClient nextBoardClient) =
                await CreateBoardSessionAsync(normalizedServerUrl, normalizedBoardId).ConfigureAwait(false);

            previousBoardStateService = boardStateService;
            previousBoardStore = boardStore;

            boardStateService = nextBoardStateService;
            boardStore = nextBoardStore;
            boardClient = nextBoardClient;
            started = true;

            LogStartup($"Board session rebound. Board={normalizedBoardId}; Server={normalizedServerUrl}");
        }
        finally
        {
            sessionGate.Release();
        }

        SessionChanged?.Invoke(this, new BoardSessionChangedEventArgs(normalizedBoardId, normalizedServerUrl));

        previousBoardStore?.Dispose();
        if (previousBoardStateService is not null)
        {
            await previousBoardStateService.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Relaunches the app so the hosted board session rebinds to the persisted board id / server URL —
    /// the WinUI analog of the frontend's <c>window.location.reload()</c> after a board switch.
    /// </summary>
    public void RequestRestart()
    {
        LogStartup("Board switch requested app relaunch.");
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }

    private async Task<(HostedBoardStateService BoardStateService, BoardStore BoardStore, EmbeddedBoardClient BoardClient)> CreateBoardSessionAsync(string serverUrl, string boardId)
    {
        if (appConfig is null)
        {
            throw new InvalidOperationException("App config not initialized.");
        }

        var nextBoardStateService = new HostedBoardStateService(serverUrl, boardId);
        LogStartup("Hosted board state service created.");
        await nextBoardStateService.StartAsync().ConfigureAwait(false);
        LogStartup("Hosted board state service started.");

        BoardStore nextBoardStore = new(nextBoardStateService, appConfig.Frontend.BoardServerConstants);
        EmbeddedBoardClient nextBoardClient = new(nextBoardStateService);
        return (nextBoardStateService, nextBoardStore, nextBoardClient);
    }

    private static string NormalizeRequired(string? value, string message)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 ? throw new InvalidOperationException(message) : normalized;
    }

    private static string NormalizeServerUrl(string? serverUrl)
    {
        string normalized = NormalizeRequired(serverUrl, "Server URL is required.");
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Server URL must be an absolute HTTP(S) URL.");
        }

        return parsedUri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeServerOrigin(string? serverOrigin)
    {
        return string.IsNullOrWhiteSpace(serverOrigin)
            ? string.Empty
            : serverOrigin.Trim().TrimEnd('/');
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