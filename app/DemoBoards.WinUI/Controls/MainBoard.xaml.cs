using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace DemoBoards_WinUI.Controls;

public sealed class MainBoard : UserControl
{
    private readonly GandalfPane GandalfPaneView;
    private readonly CentrePane CentrePaneView;
    private readonly TruthsetExplorePane TruthsetPaneView;

    public MainBoard()
    {
        GandalfPaneView = new GandalfPane();
        CentrePaneView = new CentrePane { MinHeight = 280 };
        TruthsetPaneView = new TruthsetExplorePane();

        var root = new Grid { ColumnSpacing = 18 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        root.Children.Add(GandalfPaneView);
        Grid.SetColumn(CentrePaneView, 1);
        root.Children.Add(CentrePaneView);
        Grid.SetColumn(TruthsetPaneView, 2);
        root.Children.Add(TruthsetPaneView);
        Content = root;
    }

    public void Render(BoardStore boardStore)
    {
        BoardInfoState boardInfo = boardStore.GetBoardInfo();
        BoardSummaryState summary = boardStore.State.Summary;
        BoardCanvasLayoutState layoutState = boardStore.GetCanvasLayout();
        var dataObjects = boardStore.State.DataObjectsByToken;
        string? uiConfigJson = boardStore.State.ManagedBoardConfig?.RawUiJson;
        BoardCard[] cards = boardStore.GetBoardCardDefinitionsAndData().Values
            .OrderBy(card => card.Id, System.StringComparer.Ordinal)
            .ToArray();
        var gandalfFilters = CardPresentationConfig.ResolvePaneFilters("gandalf", uiConfigJson);
        var truthsetFilters = CardPresentationConfig.ResolvePaneFilters("truthset", uiConfigJson);
        var rendererRules = CardPresentationConfig.CompileRendererRules(uiConfigJson);
        HashSet<string> gandalfIds = cards.Where(card => gandalfFilters.Any(filter => filter(card))).Select(card => card.Id).ToHashSet(System.StringComparer.Ordinal);
        HashSet<string> truthsetIds = cards.Where(card => truthsetFilters.Any(filter => filter(card))).Select(card => card.Id).ToHashSet(System.StringComparer.Ordinal);

        CentrePaneView.Render(
            boardInfo,
            summary,
            cards.Where(card => !gandalfIds.Contains(card.Id) && !truthsetIds.Contains(card.Id)).ToArray(),
            layoutState,
            dataObjects,
            rendererRules,
            layoutStrategy: "infinite-canvas");
        GandalfPaneView.Render(cards.Where(card => gandalfIds.Contains(card.Id)).ToArray(), rendererRules);
        TruthsetPaneView.Render(cards.Where(card => truthsetIds.Contains(card.Id)).ToArray(), rendererRules);
    }
}
