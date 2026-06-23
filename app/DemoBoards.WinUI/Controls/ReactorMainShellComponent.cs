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
        var (chatRequest, setChatRequest) = UseState<ChatPopoutRequest?>(null);
        var (smokeVisible, setSmokeVisible) = UseState(false);

        UseEffect(() =>
        {
            EventHandler<BoardStoreChangedEventArgs> onBoardStateChanged = (_, _) => setRevision(Guid.NewGuid().ToString("N"));
            EventHandler<BoardUiState> onBoardUiStateChanged = (_, _) => setRevision(Guid.NewGuid().ToString("N"));
            Action<ChatPopoutRequest> onChatRequested = request => setChatRequest(request);
            Action onSmokeRequested = () =>
            {
                setConfigOpen(false);
                setSmokeVisible(true);
            };

            boardStore.StateChanged += onBoardStateChanged;
            boardStore.UiStateChanged += onBoardUiStateChanged;
            ReactorShellBridge.ChatPopoutRequested += onChatRequested;
            ReactorShellBridge.SmokeRunnerRequested += onSmokeRequested;

            return () =>
            {
                boardStore.StateChanged -= onBoardStateChanged;
                boardStore.UiStateChanged -= onBoardUiStateChanged;
                ReactorShellBridge.ChatPopoutRequested -= onChatRequested;
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
        string? inspectedCardId = boardStore.UiState.InspectedCardId;

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
        else if (!string.IsNullOrWhiteSpace(inspectedCardId))
        {
            overlay = Component<ReactorGlobalModalComponent, ReactorGlobalModalProps>(
                new ReactorGlobalModalProps(
                    $"Inspect {inspectedCardId}",
                    () => boardStore.SetInspectedCardId(null),
                    Component<ReactorInspectCardComponent, ReactorInspectCardProps>(
                        new ReactorInspectCardProps(boardStore, inspectedCardId))));
        }

        else if (chatRequest is not null)
        {
            overlay = Component<ReactorGlobalModalComponent, ReactorGlobalModalProps>(
                new ReactorGlobalModalProps(
                    ResolveChatTitle(boardStore, chatRequest),
                    () => setChatRequest(null),
                    Component<ReactorChatPaneComponent, ReactorChatPaneProps>(
                        new ReactorChatPaneProps(
                            boardStore,
                            boardClient,
                            chatRequest.CardId,
                            Compact: false,
                            EnablePopout: false,
                            Title: string.IsNullOrWhiteSpace(chatRequest.Title) ? "Chat" : chatRequest.Title!))));
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

        return Border(VStack(16, sections.ToArray()))
            .Padding(16)
            .Background(ResolveBrush("BoardWindowBackgroundBrush"));
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

    private static string ResolveChatTitle(BoardStore boardStore, ChatPopoutRequest request)
    {
        BoardCard? card = boardStore.GetCardDefinitionAndData(request.CardId);
        if (!string.IsNullOrWhiteSpace(card?.Title))
        {
            return $"Chat {card.Title}";
        }

        return string.IsNullOrWhiteSpace(request.Title)
            ? $"Chat {request.CardId}"
            : request.Title!;
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
        var columns = new List<Element>();

        if (Props.GandalfCards.Count > 0)
        {
            columns.Add(Component<ReactorGandalfPaneComponent, ReactorPreviewPaneProps>(
                new ReactorPreviewPaneProps(
                    "Board Manager",
                    Props.GandalfCards,
                    Props.RendererRules,
                    ShowPhase: false,
                    EmptyMessage: "No matching cards",
                    Tone: "fresh")));
        }

        columns.Add(
            Component<ReactorCentrePaneComponent, ReactorMainBoardProps>(Props)
                .Flex(grow: 1));

        if (Props.TruthsetCards.Count > 0)
        {
            columns.Add(Component<ReactorTruthsetExplorePaneComponent, ReactorPreviewPaneProps>(
                new ReactorPreviewPaneProps(
                    "Truthset Explore",
                    Props.TruthsetCards,
                    Props.RendererRules,
                    ShowPhase: true,
                    EmptyMessage: "No matching cards",
                    Tone: "completed")));
        }

        return HStack(16, columns.ToArray()).Flex(grow: 1);
    }
}

public sealed class ReactorCentrePaneComponent : Component<ReactorMainBoardProps>
{
    public override Element Render()
    {
        return Component<ReactorBoardCanvasComponent, ReactorBoardCanvasProps>(
                new ReactorBoardCanvasProps(
                    Props.BoardInfo,
                    Props.Summary,
                    Props.CentreCards,
                    Props.LayoutState,
                    Props.DataObjects,
                    Props.RendererRules))
            .Flex(grow: 1);
    }
}

public sealed record ReactorPreviewPaneProps(
    string Title,
    IReadOnlyList<BoardCard> Cards,
    IReadOnlyList<RendererRule>? RendererRules,
    bool ShowPhase,
    string EmptyMessage,
    string Tone);

public sealed class ReactorGandalfPaneComponent : Component<ReactorPreviewPaneProps>
{
    public override Element Render()
    {
        return Component<ReactorPreviewPaneComponent, ReactorPreviewPaneProps>(Props);
    }
}

public sealed class ReactorTruthsetExplorePaneComponent : Component<ReactorPreviewPaneProps>
{
    public override Element Render()
    {
        return Component<ReactorPreviewPaneComponent, ReactorPreviewPaneProps>(Props);
    }
}

public sealed class ReactorPreviewPaneComponent : Component<ReactorPreviewPaneProps>
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(Props.Cards.Count > 0);
        var (currentIndex, setCurrentIndex) = UseState(0);

        UseEffect(() =>
        {
            if (Props.Cards.Count == 0)
            {
                setOpen(false);
            }
        }, Props.Cards.Count);

        int safeIndex = Props.Cards.Count == 0
            ? 0
            : Math.Min(currentIndex, Props.Cards.Count - 1);
        BoardCard? currentCard = Props.Cards.Count == 0 ? null : Props.Cards[safeIndex];

        var sectionItems = new List<Element>
        {
            HStack(12,
                VStack(2,
                    TextBlock(Props.Title).Bold(),
                    TextBlock(Props.Cards.Count == 0 ? Props.EmptyMessage : $"{Props.Cards.Count} cards").Opacity(0.68))
                .Flex(grow: 1),
                Button(open ? "Hide" : "Show", () => setOpen(!open))
                    .AutomationName(open ? $"Hide {Props.Title}" : $"Show {Props.Title}")
                    .SubtleButton())
        };

        if (open && currentCard is not null)
        {
            var navItems = new List<Element>
            {
                TextBlock(currentCard.Title).Bold().Flex(grow: 1),
            };

            if (Props.ShowPhase)
            {
                string phase = ResolvePhase(currentCard);
                string tone = string.Equals(phase, "done", StringComparison.OrdinalIgnoreCase) ? "completed" : Props.Tone;
                navItems.Add(
                    Border(TextBlock(phase).Bold())
                        .Background(CardToneBrushes.CreateToneBrush(tone, 0x33))
                        .WithBorder(CardToneBrushes.CreateToneBrush(tone, 0x88), 1)
                        .CornerRadius(10)
                        .Padding(8));
            }

            navItems.Add(Button("Prev", () => setCurrentIndex(Math.Max(0, safeIndex - 1))).AutomationName($"Previous {Props.Title} card").SubtleButton());
            navItems.Add(TextBlock($"{safeIndex + 1} / {Props.Cards.Count}").Opacity(0.68));
            navItems.Add(Button("Next", () => setCurrentIndex(Math.Min(Props.Cards.Count - 1, safeIndex + 1))).AutomationName($"Next {Props.Title} card").SubtleButton());

            sectionItems.Add(HStack(8, navItems.ToArray()));
            sectionItems.Add(Component<ReactorCardRendererComponent, ReactorCardRendererProps>(
                new ReactorCardRendererProps(currentCard, Props.RendererRules)));
        }

        return Border(VStack(10, sectionItems.ToArray()))
            .Padding(14)
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorSecondaryBrush"))
            .CornerRadius(14)
            .Width(352);
    }
    private static string ResolvePhase(BoardCard card)
    {
        BoardCardField? phaseField = card.Fields.FirstOrDefault(field => string.Equals(field.Key, "phase", StringComparison.OrdinalIgnoreCase));
        if (phaseField is not null && !string.IsNullOrWhiteSpace(phaseField.Value))
        {
            return phaseField.Value;
        }

        return "active";
    }
}