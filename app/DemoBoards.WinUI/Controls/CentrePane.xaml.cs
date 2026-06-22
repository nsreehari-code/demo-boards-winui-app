using DemoBoards.RuntimeHost;
using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls;

public sealed partial class CentrePane : UserControl
{
    private const string InfiniteCanvasLayout = "infinite-canvas";

    public CentrePane()
    {
        InitializeComponent();
    }

    public void Render(
        BoardInfoState boardInfo,
        BoardSummaryState summary,
        IReadOnlyList<BoardCard> cards,
        BoardCanvasLayoutState layoutState,
        IReadOnlyDictionary<string, string> dataObjects,
        IReadOnlyList<RendererRule>? rendererRules = null,
        string layoutStrategy = InfiniteCanvasLayout)
    {
        if (string.Equals(layoutStrategy, InfiniteCanvasLayout, StringComparison.OrdinalIgnoreCase))
        {
            FlowingCardsView.Visibility = Visibility.Collapsed;
            BoardCanvasView.Visibility = Visibility.Visible;
            BoardCanvasView.Render(boardInfo, summary, cards, layoutState, dataObjects, rendererRules);
            return;
        }

        BoardCanvasView.Visibility = Visibility.Collapsed;
        FlowingCardsView.Visibility = Visibility.Visible;
        RenderFlowingCards(cards, rendererRules);
    }

    private void RenderFlowingCards(IReadOnlyList<BoardCard> cards, IReadOnlyList<RendererRule>? rendererRules)
    {
        FlowGrid.Children.Clear();
        FlowGrid.RowDefinitions.Clear();
        FlowGrid.ColumnDefinitions.Clear();

        const int columnCount = 3;
        for (int column = 0; column < columnCount; column++)
        {
            FlowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        if (cards.Count == 0)
        {
            FlowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var empty = new TextBlock
            {
                Text = "No centre-pane cards available.",
                Opacity = 0.68,
                Margin = new Thickness(4)
            };
            Grid.SetColumnSpan(empty, columnCount);
            FlowGrid.Children.Add(empty);
            return;
        }

        int rowCount = (int)Math.Ceiling(cards.Count / (double)columnCount);
        for (int row = 0; row < rowCount; row++)
        {
            FlowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        foreach ((BoardCard card, int index) in cards.Select((card, index) => (card, index)))
        {
            var renderer = new CardRenderer();
            renderer.Render(card, rendererRules);
            var host = new Border
            {
                Margin = new Thickness(6),
                Child = renderer
            };

            Grid.SetRow(host, index / columnCount);
            Grid.SetColumn(host, index % columnCount);
            FlowGrid.Children.Add(host);
        }
    }
}
