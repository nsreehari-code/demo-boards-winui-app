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
        BoardStore boardStore = UseBoardStoreSubscription(includeUiState: true);
        EmbeddedBoardClient boardClient = UseEmbeddedClient();
        BoardInfoState runningBoardInfo = boardStore.GetBoardInfo();
        string runningBoardId = runningBoardInfo.BoardId;
        string runningServerUrl = NormalizeServerOrigin(boardClient.LiveBoardStateServerBaseUri.AbsoluteUri);
        var (activeBoardId, _) = UseGlobalState<string>(GlobalStateKeys.BoardId, runningBoardId);
        var (activeServerUrl, _) = UseGlobalState<string>(GlobalStateKeys.ServerUrl, boardClient.LiveBoardStateServerBaseUri.AbsoluteUri);

        var (loading, setLoading) = UseState(true);
        var (startupMessage, setStartupMessage) = UseState("Preparing board surface...");
        var (refreshingBoard, setRefreshingBoard) = UseState(false);
        var (configOpen, setConfigOpen) = UseState(false);
        var (smokeVisible, setSmokeVisible) = UseState(false);

        Action onRunTests = () =>
        {
            setConfigOpen(false);
            setSmokeVisible(true);
        };

        UseEffect(() =>
        {
            setLoading(true);
            _ = InitializeShellAsync(boardStore, boardClient, setStartupMessage, setLoading);
        }, runningBoardId, runningBoardInfo.ClientId, runningServerUrl);

        UseEffect(
            () => App.Current.ApplyThemePack(BoardTheme.ResolveThemePackIdFromUiJson(boardStore.State.ManagedBoardConfig?.RawUiJson)),
            boardStore.State.ManagedBoardConfig?.RawUiJson ?? string.Empty);

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

        var sections = new List<Element>
        {
            BuildTopBar(boardStore, boardClient, refreshingBoard, setRefreshingBoard, configOpen, setConfigOpen),
        };

        if (loading)
        {
            sections.Add(BuildStartupBanner(startupMessage));
        }

        // Board surface: the fully reactive board-tier host. BoardRenderer reads the managed config
        // itself (panes / filters / centre layout / renderer rules) and resolves the board into a node
        // tree through NodeRenderer — no imperative card-splitting in the shell.
        sections.Add(
            Component<BoardRenderer, BoardRendererProps>(
                new BoardRendererProps(boardStore.GetBoardInfo().BoardId))
            .Flex(grow: 1));

        Element? overlay = null;

        if (configOpen)
        {
            overlay = Component<GlobalModal, GlobalModalProps>(
                new GlobalModalProps(
                    "Board Settings",
                    () => setConfigOpen(false),
                    Component<ReactorAppConfigModalComponent, ReactorAppConfigModalProps>(
                        new ReactorAppConfigModalProps(
                            boardStore.GetBoardInfo().BoardId,
                            boardStore.State.ManagedBoardConfig,
                            () => setConfigOpen(false),
                            onRunTests))));
        }

        else if (smokeVisible)
        {
            overlay = Component<GlobalModal, GlobalModalProps>(
                new GlobalModalProps(
                    "Smoke Runner",
                    () => setSmokeVisible(false),
                    Component<ReactorSmokeRunnerComponent>()));
        }

        if (overlay is not null)
        {
            sections.Add(overlay);
        }

        // ThemeProvider: resolve the live app theme (from the active BoardTheme pack's resources) and
        // provide it to the whole subtree. Descendants — including the InfiniteCanvas — read it via
        // UseContext(AppThemeContext.Current) instead of looking up XAML resources themselves.
        return Border(VStack(16, sections.ToArray()))
            .Padding(16)
            .Background(ResolveBrush("BoardWindowBackgroundBrush"))
            .Provide(AppThemeContext.Current, AppTheme.FromResources());
    }

    private static Element BuildTopBar(BoardStore boardStore, EmbeddedBoardClient boardClient, bool refreshingBoard, Action<bool> setRefreshingBoard, bool configOpen, Action<bool> setConfigOpen)
    {
        (string title, string subtitle) = ResolvePageTitleAndSubtitle(boardStore.State.ManagedBoardConfig, boardStore.GetBoardInfo().BoardId);

        Element refreshButton = Button(refreshingBoard ? "Refreshing..." : "Refresh board", () =>
        {
            if (refreshingBoard)
            {
                return;
            }

            setRefreshingBoard(true);
            _ = RefreshBoardAsync(boardClient, setRefreshingBoard);
        })
        .AutomationName("Refresh board")
        .SubtleButton();

        Element configButton = Button(configOpen ? "Hide settings" : "Show settings", () => setConfigOpen(!configOpen))
            .AutomationName(configOpen ? "Hide board settings" : "Show board settings")
            .SubtleButton();

        return Border(
                HStack(12,
                    VStack(2,
                        TextBlock(title).Bold().FontSize(15),
                        TextBlock(subtitle).Opacity(0.72).FontSize(11))
                    .Flex(grow: 1),
                    refreshButton,
                    configButton))
            .Padding(14)
            .Background(ResolveBrush("BoardTopBarBackgroundBrush"))
            .WithBorder(ResolveBrush("BoardTopBarBorderBrush"), 1)
            .CornerRadius(14);
    }

    private static Element BuildStartupBanner(string startupMessage)
    {
        return Border(
                HStack(10,
                    ProgressRing(),
                    VStack(2,
                        TextBlock("Loading DemoBoards").Bold(),
                        TextBlock(startupMessage).Opacity(0.72))))
            .Padding(16)
            .Background(ResolveBrush("CardBackgroundFillColorSecondaryBrush"))
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
            App.Current.ApplyThemePack(BoardTheme.ResolveThemePackIdFromUiJson(managedConfig?.RawUiJson));
        }
        catch
        {
            boardStore.SetManagedBoardConfig(null);
            App.Current.ApplyThemePack(BoardTheme.DefaultThemePackId);
        }
        finally
        {
            setLoading(false);
        }
    }

    private static async Task RefreshBoardAsync(EmbeddedBoardClient boardClient, Action<bool> setRefreshingBoard)
    {
        try
        {
            await boardClient.RefreshBoardAsync();
        }
        finally
        {
            setRefreshingBoard(false);
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

    private static Brush ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }
}