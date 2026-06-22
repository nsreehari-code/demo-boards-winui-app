using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using DemoBoards_WinUI.State;
using DemoBoards_WinUI.Controls;
using DemoBoards.RuntimeHost;
using System;
using System.Text.Json;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DemoBoards_WinUI;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private BoardStore? boardStore;
    private EmbeddedBoardClient? boardClient;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        InspectModalView.CloseRequested += OnInspectModalCloseRequested;
        ConfigModalView.CloseRequested += OnConfigModalCloseRequested;
        ChatModalView.CloseRequested += OnChatModalCloseRequested;
        SmokeModalView.CloseRequested += OnSmokeModalCloseRequested;
    }

    public static MainPage? TryGetCurrent()
    {
        if (Application.Current is not App app || app.MainWindow is not MainWindow mainWindow)
        {
            return null;
        }

        return mainWindow.RootFrameControl.Content as MainPage;
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        boardStore = app.BoardStore;
        boardClient = app.BoardClient;
        boardStore.StateChanged += OnBoardStateChanged;
        boardStore.UiStateChanged += OnBoardUiStateChanged;

        UpdatePageChrome(boardStore);
        ShowStartupOverlay("Preparing board surface...");
        await InitializePageAsync(app);
    }

    private async Task InitializePageAsync(App app)
    {
        await Task.Yield();
        await Task.Yield();

        if (boardStore is null)
        {
            HideStartupOverlay();
            return;
        }

        ShowStartupOverlay("Rendering board...");
        RenderBoard(boardStore);

        if (boardClient is not null)
        {
            try
            {
                ShowStartupOverlay("Loading board configuration...");
                ManagedBoardConfigState? managedConfig = await boardClient.GetManagedBoardConfigAsync(boardStore.GetBoardInfo().BoardId);
                if (boardStore is null)
                {
                    HideStartupOverlay();
                    return;
                }

                boardStore.SetManagedBoardConfig(managedConfig);
                app.ApplyThemePack(BoardTheme.ResolveThemePackIdFromUiJson(managedConfig?.RawUiJson));
            }
            catch
            {
                if (boardStore is null)
                {
                    HideStartupOverlay();
                    return;
                }

                boardStore.SetManagedBoardConfig(null);
                app.ApplyThemePack(BoardTheme.DefaultThemePackId);
            }
        }

        if (boardStore is null)
        {
            HideStartupOverlay();
            return;
        }

        UpdatePageChrome(boardStore);
        HideStartupOverlay();
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (boardStore is not null)
        {
            boardStore.StateChanged -= OnBoardStateChanged;
            boardStore.UiStateChanged -= OnBoardUiStateChanged;
        }
    }

    private void OnBoardStateChanged(object? sender, BoardStoreChangedEventArgs change)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (boardStore is null) return;

            bool shouldRenderBoard = change.SummaryChanged
                || change.DataObjectsChanged
                || change.DefinitionsChanged
                || change.RuntimesChanged
                || change.ManagedConfigChanged
                || change.LayoutChanged;
            if (shouldRenderBoard)
            {
                if (change.ManagedConfigChanged)
                {
                    ((App)Application.Current).ApplyThemePack(BoardTheme.ResolveThemePackIdFromUiJson(boardStore.State.ManagedBoardConfig?.RawUiJson));
                }

                UpdatePageChrome(boardStore);
                RenderBoard(boardStore);
            }

            string? inspectedCardId = boardStore.UiState.InspectedCardId;
            bool shouldRenderInspect = !string.IsNullOrWhiteSpace(inspectedCardId)
                && change.ChangedCardIds.Contains(inspectedCardId)
                && (change.DataObjectsChanged
                    || change.DefinitionsChanged
                    || change.RuntimesChanged);
            if (shouldRenderInspect)
            {
                RenderInspectState(boardStore);
            }
        });
    }

    private void RenderBoard(BoardStore store)
    {
        MainBoardView.Render(store);
    }

    private void UpdatePageChrome(BoardStore store)
    {
        (string title, string subtitle) = ResolvePageTitleAndSubtitle(store.State.ManagedBoardConfig, store.GetBoardInfo().BoardId);
        PageTitleText.Text = title;
        PageSubtitleText.Text = subtitle;
    }

    private void ShowStartupOverlay(string message)
    {
        StartupStatusText.Text = message;
        StartupOverlay.Visibility = Visibility.Visible;
    }

    private void HideStartupOverlay()
    {
        StartupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnBoardUiStateChanged(object? sender, BoardUiState uiState)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (boardStore is not null)
            {
                RenderBoard(boardStore);
                RenderInspectState(boardStore);
            }
        });
    }

    private void RenderInspectState(BoardStore store)
    {
        string? inspectedCardId = store.UiState.InspectedCardId;
        if (string.IsNullOrWhiteSpace(inspectedCardId))
        {
            InspectModalView.Hide();
            return;
        }

        var inspectCard = new InspectCard();
        inspectCard.Render(store, inspectedCardId);
        InspectModalView.Show($"Inspect {inspectedCardId}", inspectCard);
    }

    private void OnInspectModalCloseRequested(object? sender, EventArgs e)
    {
        boardStore?.SetInspectedCardId(null);
    }

    private void OnConfigModalCloseRequested(object? sender, EventArgs e)
    {
        ConfigModalView.Hide();
    }

    private void OnChatModalCloseRequested(object? sender, EventArgs e)
    {
        ChatModalView.Hide();
    }

    private void OnSmokeModalCloseRequested(object? sender, EventArgs e)
    {
        SmokeModalView.Hide();
    }

    private async void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (boardClient is null) return;
        await boardClient.RefreshBoardAsync();
    }

    private void OnBoardConfigClick(object sender, RoutedEventArgs e)
    {
        if (boardStore is null)
        {
            return;
        }

        var modal = new AppConfigModal();
        modal.Render(boardStore.GetBoardInfo().BoardId, boardStore.State.ManagedBoardConfig);
        ConfigModalView.Show("Board Configuration", modal);
    }

    public void ShowChatPopout(string cardId, string? title = null)
    {
        if (boardStore is null || boardClient is null || string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        BoardCard? card = boardStore.GetCardDefinitionAndData(cardId);
        var chatPane = new ChatPane();
        chatPane.Configure(compact: false, enablePopout: false, title: string.IsNullOrWhiteSpace(title) ? "Chat" : title);
        chatPane.Bind(boardStore, boardClient, cardId);
        string modalTitle = string.IsNullOrWhiteSpace(card?.Title)
            ? $"Chat {cardId}"
            : $"Chat {card.Title}";
        ChatModalView.Show(modalTitle, chatPane);
    }

    public void ShowSmokeRunner()
    {
        var runner = new SmokeRunner();
        SmokeModalView.Show("Smoke Runner", runner);
    }

    private static (string Title, string Subtitle) ResolvePageTitleAndSubtitle(ManagedBoardConfigState? config, string boardId)
    {
        string fallbackTitle = string.IsNullOrWhiteSpace(boardId) ? "Demo Boards" : boardId;
        string fallbackSubtitle = "Embedded board workspace";
        if (config is null || string.IsNullOrWhiteSpace(config.RawMetadataJson))
        {
            return (fallbackTitle, fallbackSubtitle);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(config.RawMetadataJson);
            JsonElement root = document.RootElement;
            string title = root.TryGetProperty("pageTitle", out JsonElement pageTitle)
                && pageTitle.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(pageTitle.GetString())
                    ? pageTitle.GetString()!
                    : fallbackTitle;
            string subtitle = root.TryGetProperty("pageSubtitle", out JsonElement pageSubtitle)
                && pageSubtitle.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(pageSubtitle.GetString())
                    ? pageSubtitle.GetString()!
                    : fallbackSubtitle;
            return (title, subtitle);
        }
        catch
        {
            return (fallbackTitle, fallbackSubtitle);
        }
    }
}
