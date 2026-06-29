using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using DemoBoards_WinUI.Controls.Registry;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record AppRootProps();

public sealed class AppRoot : HookComponent<AppRootProps>
{
    public override Element Render()
    {
        AppTheme theme = AppTheme.FromResources();
        BoardStore boardStore = UseBoardStoreSubscription(includeUiState: true);
        EmbeddedBoardClient boardClient = UseEmbeddedClient();
        ManageBoards manageBoards = UseManageBoards();
        BoardInfoState runningBoardInfo = boardStore.GetBoardInfo();
        string runningBoardId = runningBoardInfo.BoardId;
        BoardState board = UseBoardState(runningBoardId);
        BoardVisuals visualsHook = UseBoardVisuals(runningBoardId);
        string runningServerUrl = NormalizeServerOrigin(boardClient.LiveBoardStateServerBaseUri.AbsoluteUri);
        var (activeBoardId, setActiveBoardId) = UseGlobalState<string>(GlobalStateKeys.BoardId, runningBoardId);
        var (activeServerUrl, setActiveServerUrl) = UseGlobalState<string>(GlobalStateKeys.ServerUrl, boardClient.LiveBoardStateServerBaseUri.AbsoluteUri);
        var (testPageMode, _) = UseGlobalState<bool>(GlobalStateKeys.TestPageMode, false);

        var (loading, setLoading) = UseState(true);
        var (startupMessage, setStartupMessage) = UseState("Preparing board surface...");
        var (configOpen, setConfigOpen) = UseState(false);
        var (smokeRunnerVisible, setSmokeRunnerVisible) = UseState(false);

        Action onRunSmokeRunner = () =>
        {
            setConfigOpen(false);
            setSmokeRunnerVisible(true);
        };

        UseEffect(() =>
        {
            setLoading(true);
            _ = InitializeShellAsync(boardStore, boardClient, setStartupMessage, setLoading);
        }, runningBoardId, runningBoardInfo.ClientId, runningServerUrl);

        UseEffect(
            () => App.Current.ApplyThemePack(visualsHook.Visuals.Theme),
            visualsHook.Visuals.Theme);

        UseEffect(() =>
        {
            string targetBoardId = (activeBoardId ?? string.Empty).Trim();
            string targetServerUrl = NormalizeServerOrigin(activeServerUrl);

            if (targetBoardId.Length == 0 || targetServerUrl.Length == 0)
            {
                return () => { };
            }

            if (!string.Equals(runningBoardId, targetBoardId, StringComparison.Ordinal)
                || !string.Equals(runningServerUrl, targetServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                bool cancelled = false;

                setLoading(true);
                setStartupMessage("Rebinding board session...");

                async Task RebindAsync()
                {
                    try
                    {
                        await App.Current.RebindBoardSessionAsync(targetServerUrl, targetBoardId).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (cancelled)
                        {
                            return;
                        }

                        setStartupMessage($"Failed to rebind board session: {ex.Message}");
                        setLoading(false);
                    }
                }

                _ = RebindAsync();
                return () => { cancelled = true; };
            }

            return () => { };
        }, activeBoardId, activeServerUrl, runningBoardId, runningServerUrl);

        var sections = new List<Element>();

        if (!testPageMode)
        {
            sections.Add(
                BuildTopBar(
                    boardStore,
                    board,
                    theme));
        }

        if (loading)
        {
            sections.Add(BuildStartupBanner(startupMessage, theme));
        }

        // Board surface: the fully reactive board-tier host. BoardRenderer reads the managed config
        // itself (panes / filters / centre layout / renderer rules) and resolves the board into a node
        // tree through NodeRenderer — no imperative card-splitting in the shell.
        sections.Add(
            (testPageMode
                ? (Element)Component<InfiniteCanvasWorkingExamplePage, InfiniteCanvasWorkingExamplePageProps>(
                    new InfiniteCanvasWorkingExamplePageProps())
                : Component<BoardRenderer, BoardRendererProps>(
                    new BoardRendererProps(boardStore.GetBoardInfo().BoardId)))
            .Flex(grow: 1)
            .Margin(0, testPageMode ? 0 : 4, 0, 0));

        Element boardSettingsHost = Component<PanelVertical, PanelVerticalProps>(
            new PanelVerticalProps(
                FabPosition: "bottom-right",
                Expanded: configOpen,
                OnToggle: () => setConfigOpen(!configOpen),
                AriaLabel: "Board settings",
                Title: configOpen ? "Close board settings" : "Board settings",
                Icon: "bi-gear-fill",
                IconToggled: "bi-x-lg",
                Children: Component<BoardConfigPane, BoardConfigPaneProps>(
                    new BoardConfigPaneProps(
                        BoardId: boardStore.GetBoardInfo().BoardId,
                        CloseAction: () => setConfigOpen(false),
                        BoardClient: boardClient,
                        ManageBoards: manageBoards,
                        ActiveBoardId: activeBoardId,
                        SetActiveBoardId: setActiveBoardId,
                        ActiveServerUrl: activeServerUrl,
                        SetActiveServerUrl: setActiveServerUrl,
                        InitialServerUrl: App.Current.HostConfig.Frontend.InitialServerUrl,
                        LiveRuntimeServerUrl: boardClient.LiveBoardStateServerBaseUri.AbsoluteUri,
                        SetManagedBoardConfig: boardStore.SetManagedBoardConfig,
                        OnRunSmokeRunner: onRunSmokeRunner))));

        Element? overlay = null;

        if (smokeRunnerVisible)
        {
            overlay = Component<GlobalModal, GlobalModalProps>(
                new GlobalModalProps(
                    "Smoke Runner",
                    () => setSmokeRunnerVisible(false),
                    Component<ReactorSmokeRunnerComponent>()));
        }

        Element shell = Border(VStack(0, sections.ToArray()))
            .Background(theme.WindowBackground);

        // ThemeProvider: resolve the live app theme (from the active BoardTheme pack's resources) and
        // provide it to the whole subtree. Descendants — including the InfiniteCanvas — read it via
        // UseContext(AppThemeContext.Current) instead of looking up XAML resources themselves.
        return Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                shell,
                boardSettingsHost,
                overlay is null
                    ? Empty()
                    : overlay
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center)
                        .Margin(24))
                .Provide(AppThemeContext.Current, theme);
    }

    private static Element BuildTopBar(
        BoardStore boardStore,
        BoardState board,
        AppTheme theme)
    {
        (string title, string subtitle) = ResolvePageTitleAndSubtitle(boardStore.State.ManagedBoardConfig, boardStore.GetBoardInfo().BoardId);
        double refreshIntervalMs = ResolveRefreshAllIntervalMs(boardStore.State.ManagedBoardConfig);

        var titleBlockChildren = new List<Element>
        {
            TextBlock(title).Bold().FontSize(15)
        };

        Element refreshButton = Component<TimerButton, TimerButtonProps>(
            new TimerButtonProps(
                Duration: refreshIntervalMs,
                OnClick: async () =>
                {
                    await board.BoardActions.RefreshAll().ConfigureAwait(false);
                },
                Disabled: !board.HasRefreshableCards,
                Width: 112,
                Children: state => HStack(8,
                    TextBlock(state.Pending ? "Refreshing..." : FormatCountdown(state.RemainingMs))
                        .Foreground(theme.TextPrimary))));

        Element actions = HStack(12,
                refreshButton)
            .HorizontalAlignment(HorizontalAlignment.Right);

        return Border(
                Grid(
                    new[] { GridSize.Star(), GridSize.Auto },
                    new[] { GridSize.Auto },
                    VStack(2,
                            HStack(8, titleBlockChildren.ToArray())
                                .VAlign(VerticalAlignment.Center),
                            TextBlock(subtitle).Opacity(0.72).FontSize(11))
                        .Grid(0, 0),
                    actions
                        .Grid(0, 1)
                        .VAlign(VerticalAlignment.Center))
                .HorizontalAlignment(HorizontalAlignment.Stretch))
            .Padding(12, 8, 12, 8)
                .Background(theme.TopBarBackground)
                .Set(border =>
                {
                    border.BorderBrush = theme.TopBarBorder;
                    border.BorderThickness = new Thickness(0, 0, 0, 1);
                })
            .HorizontalAlignment(HorizontalAlignment.Stretch);
    }

    private static Element BuildStartupBanner(string startupMessage, AppTheme theme)
    {
        return Border(
                HStack(10,
                    ProgressRing(),
                    VStack(2,
                        TextBlock("Loading DemoBoards").Bold(),
                        TextBlock(startupMessage).Opacity(0.72))))
            .Padding(16)
                .Background(theme.SecondaryCardBackground)
            .CornerRadius(14);
    }

    private static async Task InitializeShellAsync(BoardStore boardStore, EmbeddedBoardClient boardClient, Action<string> setStartupMessage, Action<bool> setLoading)
    {
        setStartupMessage("Rendering board...");
        await Task.Yield();

        try
        {
            setStartupMessage("Loading board configuration...");
            ManagedBoardConfigState? managedConfig = await boardClient.GetManagedBoardConfigAsync(boardStore.GetBoardInfo().BoardId);
            boardStore.SetManagedBoardConfig(managedConfig);
        }
        catch
        {
            boardStore.SetManagedBoardConfig(null);
        }
        finally
        {
            setLoading(false);
        }
    }

    private static string NormalizeServerOrigin(string? serverOrigin)
    {
        return string.IsNullOrWhiteSpace(serverOrigin)
            ? string.Empty
            : serverOrigin.Trim().TrimEnd('/');
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

    private static double ResolveRefreshAllIntervalMs(ManagedBoardConfigState? config)
    {
        const int defaultRefreshAllIntervalSeconds = 30 * 60;

        if (config is null || string.IsNullOrWhiteSpace(config.RawMetadataJson))
        {
            return defaultRefreshAllIntervalSeconds * 1000d;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(config.RawMetadataJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("refreshAllIntervalSeconds", out JsonElement secondsElement)
                && secondsElement.ValueKind == JsonValueKind.Number
                && secondsElement.TryGetInt32(out int seconds)
                && seconds > 0)
            {
                return seconds * 1000d;
            }

            if (root.TryGetProperty("refreshAllIntervalMs", out JsonElement millisecondsElement)
                && millisecondsElement.ValueKind == JsonValueKind.Number
                && millisecondsElement.TryGetInt32(out int milliseconds)
                && milliseconds > 0)
            {
                return milliseconds;
            }
        }
        catch
        {
        }

        return defaultRefreshAllIntervalSeconds * 1000d;
    }

    private static string FormatCountdown(double remainingMs)
    {
        int totalSeconds = Math.Max(0, (int)Math.Ceiling(remainingMs / 1000d));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }
}