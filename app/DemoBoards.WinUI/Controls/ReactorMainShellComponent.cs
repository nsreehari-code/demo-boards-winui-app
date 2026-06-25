using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorMainBoardProps(
    BoardInfoState BoardInfo,
    BoardSummaryState Summary,
    IReadOnlyList<BoardCard> CentreCards,
    IReadOnlyList<BoardCard> GandalfCards,
    IReadOnlyList<BoardCard> TruthsetCards,
    BoardCanvasLayoutState LayoutState,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<RendererRule>? RendererRules);

public sealed class ReactorMainShellComponent : Component
{
    public override Element Render()
    {
        BoardStore boardStore = App.Current.BoardStore;
        EmbeddedBoardClient boardClient = App.Current.BoardClient;

        var (_, setRevision) = UseState(string.Empty);
        var (loading, setLoading) = UseState(true);
        var (startupMessage, setStartupMessage) = UseState("Preparing board surface...");
        var (refreshingBoard, setRefreshingBoard) = UseState(false);
        var (configOpen, setConfigOpen) = UseState(false);
        var (smokeVisible, setSmokeVisible) = UseState(false);

        UseEffect(() =>
        {
            EventHandler<BoardStoreChangedEventArgs> onBoardStateChanged = (_, _) => setRevision(Guid.NewGuid().ToString("N"));
            EventHandler<BoardUiState> onBoardUiStateChanged = (_, _) => setRevision(Guid.NewGuid().ToString("N"));
            Action onSmokeRequested = () =>
            {
                setConfigOpen(false);
                setSmokeVisible(true);
            };

            boardStore.StateChanged += onBoardStateChanged;
            boardStore.UiStateChanged += onBoardUiStateChanged;
            ReactorShellBridge.SmokeRunnerRequested += onSmokeRequested;

            return () =>
            {
                boardStore.StateChanged -= onBoardStateChanged;
                boardStore.UiStateChanged -= onBoardUiStateChanged;
                ReactorShellBridge.SmokeRunnerRequested -= onSmokeRequested;
            };
        });

        UseEffect(() =>
        {
            _ = InitializeShellAsync(boardStore, boardClient, setStartupMessage, setLoading);
        });

        UseEffect(
            () => App.Current.ApplyThemePack(BoardTheme.ResolveThemePackIdFromUiJson(boardStore.State.ManagedBoardConfig?.RawUiJson)),
            boardStore.State.ManagedBoardConfig?.RawUiJson ?? string.Empty);

        BoardCard[] allCards = boardStore.GetBoardCardDefinitionsAndData().Values
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .ToArray();
        string? uiConfigJson = boardStore.State.ManagedBoardConfig?.RawUiJson;
        var gandalfFilters = CardPresentationConfig.ResolvePaneFilters("gandalf", uiConfigJson);
        var truthsetFilters = CardPresentationConfig.ResolvePaneFilters("truthset", uiConfigJson);
        var rendererRules = CardPresentationConfig.CompileRendererRules(uiConfigJson);
        HashSet<string> gandalfIds = allCards
            .Where(card => gandalfFilters.Any(filter => filter(card)))
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> truthsetIds = allCards
            .Where(card => truthsetFilters.Any(filter => filter(card)))
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        BoardCard[] centreCards = allCards.Where(card => !gandalfIds.Contains(card.Id) && !truthsetIds.Contains(card.Id)).ToArray();
        BoardCard[] gandalfCards = allCards.Where(card => gandalfIds.Contains(card.Id)).ToArray();
        BoardCard[] truthsetCards = allCards.Where(card => truthsetIds.Contains(card.Id)).ToArray();

        var sections = new List<Element>
        {
            BuildTopBar(boardStore, refreshingBoard, setRefreshingBoard, configOpen, setConfigOpen),
        };

        if (loading)
        {
            sections.Add(BuildStartupBanner(startupMessage));
        }

        sections.Add(
            Component<ReactorMainBoardComponent, ReactorMainBoardProps>(
                new ReactorMainBoardProps(
                    boardStore.GetBoardInfo(),
                    boardStore.State.Summary,
                    centreCards,
                    gandalfCards,
                    truthsetCards,
                    boardStore.GetCanvasLayout(),
                    boardStore.State.DataObjectsByToken,
                    rendererRules))
            .Flex(grow: 1));

        Element? overlay = null;

        if (configOpen)
        {
            overlay = Component<ReactorGlobalModalComponent, ReactorGlobalModalProps>(
                new ReactorGlobalModalProps(
                    "Board Settings",
                    () => setConfigOpen(false),
                    Component<ReactorAppConfigModalComponent, ReactorAppConfigModalProps>(
                        new ReactorAppConfigModalProps(
                            boardStore.GetBoardInfo().BoardId,
                            boardStore.State.ManagedBoardConfig,
                            () => setConfigOpen(false)))));
        }

        else if (smokeVisible)
        {
            overlay = Component<ReactorGlobalModalComponent, ReactorGlobalModalProps>(
                new ReactorGlobalModalProps(
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

    private static Element BuildTopBar(BoardStore boardStore, bool refreshingBoard, Action<bool> setRefreshingBoard, bool configOpen, Action<bool> setConfigOpen)
    {
        (string title, string subtitle) = ResolvePageTitleAndSubtitle(boardStore.State.ManagedBoardConfig, boardStore.GetBoardInfo().BoardId);

        Element refreshButton = Button(refreshingBoard ? "Refreshing..." : "Refresh board", () =>
        {
            if (refreshingBoard)
            {
                return;
            }

            setRefreshingBoard(true);
            _ = RefreshBoardAsync(setRefreshingBoard);
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

    private static async Task RefreshBoardAsync(Action<bool> setRefreshingBoard)
    {
        try
        {
            await App.Current.BoardClient.RefreshBoardAsync();
        }
        finally
        {
            setRefreshingBoard(false);
        }
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

    internal static Brush ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }
}

public sealed class ReactorMainBoardComponent : Component<ReactorMainBoardProps>
{
    public override Element Render()
    {
        return Component<ReactorCentrePaneComponent, ReactorMainBoardProps>(Props)
            .Flex(grow: 1);
    }
}

public sealed class ReactorCentrePaneComponent : Component<ReactorMainBoardProps>
{
    public override Element Render()
    {
        return Component<ReactorInfiniteCanvasComponent, ReactorInfiniteCanvasProps>(
                new ReactorInfiniteCanvasProps(
                    Props.BoardInfo,
                    Props.Summary,
                    Props.CentreCards,
                    Props.LayoutState,
                    Props.DataObjects,
                    Props.RendererRules))
            .Flex(grow: 1);
    }
}