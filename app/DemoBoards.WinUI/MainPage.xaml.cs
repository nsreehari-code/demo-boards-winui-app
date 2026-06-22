using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using DemoBoards_WinUI.State;
using DemoBoards_WinUI.Controls;
using DemoBoards.RuntimeHost;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace DemoBoards_WinUI;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed class MainPage : Page
{
    private const int DefaultRefreshAllIntervalSeconds = 30 * 60;

    private readonly TextBlock PageTitleText;
    private readonly TextBlock PageSubtitleText;
    private readonly TimerButton RefreshTimerButton;
    private readonly MainBoard MainBoardView;
    private readonly GlobalModal InspectModalView;
    private readonly GlobalModal ChatModalView;
    private readonly GlobalModal SmokeModalView;
    private readonly PanelRail ConfigPanelView;
    private readonly Grid StartupOverlay;
    private readonly TextBlock StartupStatusText;
    private AppConfigModal? configPanelContent;

    private BoardStore? boardStore;
    private EmbeddedBoardClient? boardClient;
    private readonly DispatcherTimer refreshCountdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset nextRefreshAt = DateTimeOffset.UtcNow.AddSeconds(DefaultRefreshAllIntervalSeconds);
    private int refreshAllIntervalSeconds = DefaultRefreshAllIntervalSeconds;
    private bool refreshingBoard;

    public MainPage()
    {
        PageTitleText = new TextBlock
        {
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ResolveBrush("BoardTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        PageSubtitleText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.92,
            Foreground = ResolveBrush("BoardTextMutedBrush"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        RefreshTimerButton = new TimerButton();
        RefreshTimerButton.Click += OnRefreshClick;
        MainBoardView = new MainBoard();
        InspectModalView = new GlobalModal();
        ChatModalView = new GlobalModal();
        SmokeModalView = new GlobalModal();
        ConfigPanelView = new PanelRail();
        ConfigPanelView.Configure(new PanelRailOptions(
            Side: PanelRailSide.Right,
            ButtonPosition: FloatingButtonPosition.BottomRight,
            PanelWidth: 448,
            OpenToolTipText: "Open Board Settings",
            CloseToolTipText: "Close Board Settings",
            OpenGlyph: string.Empty,
            CloseGlyph: "\uE711",
            OpenSvgIconPath: "Assets/Icons/appconfig-settings.svg",
            ButtonInset: 18,
            ButtonDiameter: 48,
            WrapContentInScrollViewer: false,
            ButtonStyle: ResolveStyle("BoardFloatingCircleButtonStyle"),
            ActiveButtonStyle: ResolveStyle("BoardFloatingCircleButtonActiveStyle"),
            VisualStyle: new PanelRailVisualStyle(
                PanelBackground: CreateVerticalGradientBrush(Windows.UI.Color.FromArgb(0xFD, 0xE2, 0xEA, 0xF1), Windows.UI.Color.FromArgb(0xFD, 0xD6, 0xDF, 0xE8)),
                PanelBorderBrush: CreateSolidBrush(Windows.UI.Color.FromArgb(0x3D, 0x40, 0x60, 0x83)),
                OverlayBrush: ResolveBrush("BoardOverlayBrush"),
                PanelBorderThickness: new Thickness(1),
                PanelCornerRadius: new CornerRadius(24, 0, 0, 24))));
        ConfigPanelView.Opening += OnConfigPanelOpening;
        StartupStatusText = new TextBlock
        {
            Text = "Preparing board surface...",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ResolveBrush("BoardTextMutedBrush"),
        };
        StartupOverlay = BuildStartupOverlay();

        Content = BuildPageContent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        refreshCountdownTimer.Tick += OnRefreshCountdownTick;
        InspectModalView.CloseRequested += OnInspectModalCloseRequested;
        ChatModalView.CloseRequested += OnChatModalCloseRequested;
        SmokeModalView.CloseRequested += OnSmokeModalCloseRequested;
        RefreshTimerButton.Label = string.Empty;
        RefreshTimerButton.ToolTipText = "Refresh all cards";
    }

    private UIElement BuildPageContent()
    {
        var titleStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(PageTitleText);
        titleStack.Children.Add(PageSubtitleText);

        var refreshStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        refreshStack.Children.Add(RefreshTimerButton);

        var topBarGrid = new Grid { ColumnSpacing = 10, MinHeight = 48 };
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.Children.Add(titleStack);
        Grid.SetColumn(refreshStack, 1);
        topBarGrid.Children.Add(refreshStack);

        var topBar = new Border
        {
            Padding = new Thickness(14, 6, 14, 6),
            Background = ResolveBrush("BoardTopBarBackgroundBrush"),
            BorderBrush = ResolveBrush("BoardTopBarBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = topBarGrid,
        };

        var contentGrid = new Grid { RowSpacing = 0 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentGrid.Children.Add(topBar);
        Grid.SetRow(MainBoardView, 1);
        contentGrid.Children.Add(MainBoardView);

        var root = new Grid { Background = ResolveBrush("BoardWindowBackgroundBrush") };
        root.Children.Add(contentGrid);
        root.Children.Add(InspectModalView);
        root.Children.Add(ChatModalView);
        root.Children.Add(SmokeModalView);
        root.Children.Add(ConfigPanelView);
        root.Children.Add(StartupOverlay);
        return root;
    }

    private Grid BuildStartupOverlay()
    {
        var statusStack = new StackPanel { Spacing = 14, Width = 320 };
        statusStack.Children.Add(new ProgressRing { IsActive = true, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center });
        statusStack.Children.Add(new TextBlock
        {
            Text = "Loading DemoBoards",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ResolveBrush("BoardTextBrush"),
        });
        statusStack.Children.Add(StartupStatusText);

        return new Grid
        {
            Background = ResolveBrush("BoardWindowBackgroundBrush"),
            Children =
            {
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(24),
                    CornerRadius = new CornerRadius(18),
                    Background = ResolveBrush("CardBackgroundFillColorSecondaryBrush"),
                    Child = statusStack,
                }
            }
        };
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
        ResetRefreshCountdown();
        UpdateRefreshTimerState();
        if (!refreshCountdownTimer.IsEnabled)
        {
            refreshCountdownTimer.Start();
        }

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
        refreshCountdownTimer.Stop();
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
                UpdateRefreshIntervalFromConfig(boardStore.State.ManagedBoardConfig);
                UpdateRefreshTimerState();
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

    private void UpdateRefreshTimerState()
    {
        bool canRefreshAll = CanRefreshAll();
        TimeSpan remaining = nextRefreshAt - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        RefreshTimerButton.TimeText = FormatCountdown(remaining);
        RefreshTimerButton.IsBusy = refreshingBoard;
        RefreshTimerButton.IsActionEnabled = canRefreshAll;
    }

    private void ResetRefreshCountdown()
    {
        nextRefreshAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, refreshAllIntervalSeconds));
        UpdateRefreshTimerState();
    }

    private void UpdateRefreshIntervalFromConfig(ManagedBoardConfigState? config)
    {
        int nextIntervalSeconds = ResolveRefreshAllIntervalSeconds(config);
        if (nextIntervalSeconds == refreshAllIntervalSeconds)
        {
            return;
        }

        refreshAllIntervalSeconds = nextIntervalSeconds;
        ResetRefreshCountdown();
    }

    private bool CanRefreshAll()
    {
        if (boardStore is null)
        {
            return false;
        }

        foreach (string cardId in boardStore.GetBoardCardIds())
        {
            if (boardStore.GetCardState(cardId)?.CanRefresh == true)
            {
                return true;
            }
        }

        return false;
    }

    private async void OnRefreshCountdownTick(object? sender, object e)
    {
        UpdateRefreshTimerState();
        if (refreshingBoard || !CanRefreshAll() || DateTimeOffset.UtcNow < nextRefreshAt)
        {
            return;
        }

        await RefreshBoardAsync();
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
        await RefreshBoardAsync();
    }

    private async Task RefreshBoardAsync()
    {
        if (boardClient is null || refreshingBoard)
        {
            return;
        }

        refreshingBoard = true;
        UpdateRefreshTimerState();
        try
        {
            await boardClient.RefreshBoardAsync();
        }
        finally
        {
            refreshingBoard = false;
            ResetRefreshCountdown();
        }
    }

    private void OnConfigPanelOpening(object? sender, EventArgs e)
    {
        if (boardStore is null)
        {
            return;
        }

        if (configPanelContent is null)
        {
            configPanelContent = new AppConfigModal();
            configPanelContent.CloseRequested += OnConfigPanelCloseRequested;
        }

        configPanelContent.Render(boardStore.GetBoardInfo().BoardId, boardStore.State.ManagedBoardConfig);
        ConfigPanelView.SetPanelContent(configPanelContent);
    }

    private void OnConfigPanelCloseRequested(object? sender, EventArgs e)
    {
        ConfigPanelView.SetOpen(false);
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

    private static int ResolveRefreshAllIntervalSeconds(ManagedBoardConfigState? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.RawMetadataJson))
        {
            return DefaultRefreshAllIntervalSeconds;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(config.RawMetadataJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("refreshAllIntervalSeconds", out JsonElement refreshSeconds)
                && refreshSeconds.ValueKind is JsonValueKind.Number
                && refreshSeconds.TryGetInt32(out int seconds)
                && seconds > 0)
            {
                return seconds;
            }

            if (root.TryGetProperty("refreshAllIntervalMs", out JsonElement refreshMs)
                && refreshMs.ValueKind is JsonValueKind.Number
                && refreshMs.TryGetInt32(out int milliseconds)
                && milliseconds > 0)
            {
                return Math.Max(1, milliseconds / 1000);
            }
        }
        catch
        {
        }

        return DefaultRefreshAllIntervalSeconds;
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }

    private static Style? ResolveStyle(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Style
            : null;
    }

    private static Brush CreateSolidBrush(Windows.UI.Color color)
    {
        return new SolidColorBrush(color);
    }

    private static Brush CreateVerticalGradientBrush(Windows.UI.Color start, Windows.UI.Color end)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        return brush;
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        int totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }
}
