using System.Collections.Generic;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class GandalfPane : UserControl
{
    private readonly PanelRail panelView;
    private readonly TextBlock countText;
    private readonly TextBlock currentTitleText;
    private readonly Button prevButton;
    private readonly TextBlock counterText;
    private readonly Button nextButton;
    private readonly CardRenderer previewCardView;

    private readonly List<BoardCard> cards = new();
    private int currentIndex;
    private IReadOnlyList<RendererRule>? currentRendererRules;

    public GandalfPane()
    {
        panelView = new PanelRail();
        panelView.Configure(new PanelRailOptions(
            Side: PanelRailSide.Left,
            ButtonPosition: FloatingButtonPosition.TopLeft,
            PanelWidth: 368,
            OpenToolTipText: "Open Board Manager",
            CloseToolTipText: "Close Board Manager",
            OpenGlyph: "\uE76C",
            CloseGlyph: "\uE76B",
            ButtonInset: 0,
            ButtonOffsetX: -10,
            ButtonOffsetY: 8,
            ButtonDiameter: 48,
            WrapContentInScrollViewer: true,
            ButtonStyle: ResolveStyle("BoardEdgeToggleButtonStyle"),
            ActiveButtonStyle: ResolveStyle("BoardEdgeToggleButtonActiveStyle")));

        countText = new TextBlock { FontSize = 12, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
        currentTitleText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        prevButton = new Button { Content = "Prev", HorizontalAlignment = HorizontalAlignment.Right, Style = ResolveStyle("BoardToolbarButtonStyle") };
        prevButton.Click += OnPrevClick;
        counterText = new TextBlock { FontSize = 12, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        nextButton = new Button { Content = "Next", HorizontalAlignment = HorizontalAlignment.Right, Style = ResolveStyle("BoardToolbarButtonStyle") };
        nextButton.Click += OnNextClick;
        previewCardView = new CardRenderer();

        panelView.SetPanelContent(BuildPanelContent());
        Content = panelView;
    }

    public void Render(IReadOnlyList<BoardCard> nextCards, IReadOnlyList<RendererRule>? rendererRules = null)
    {
        string? previousCardId = currentIndex >= 0 && currentIndex < cards.Count ? cards[currentIndex].Id : null;
        cards.Clear();
        if (nextCards is not null)
        {
            cards.AddRange(nextCards);
        }

        currentIndex = ResolveNextIndex(previousCardId, cards, currentIndex);
        currentRendererRules = rendererRules;
        countText.Text = cards.Count == 0 ? "No matching cards" : $"{cards.Count} cards";
        panelView.ToggleVisibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderCurrentCard();
    }

    private UIElement BuildPanelContent()
    {
        var headerGrid = new Grid { Padding = new Thickness(16, 14, 16, 12) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new TextBlock
        {
            Text = "Board Manager",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ResolveBrush("BoardAccentStrongBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(countText, 1);
        headerGrid.Children.Add(countText);

        var navGrid = new Grid { Padding = new Thickness(16, 12, 16, 12), ColumnSpacing = 10 };
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.Children.Add(currentTitleText);
        Grid.SetColumn(prevButton, 1);
        navGrid.Children.Add(prevButton);
        Grid.SetColumn(counterText, 2);
        navGrid.Children.Add(counterText);
        Grid.SetColumn(nextButton, 3);
        navGrid.Children.Add(nextButton);

        return new StackPanel
        {
            Spacing = 0,
            Children =
            {
                headerGrid,
                navGrid,
                previewCardView,
            }
        };
    }

    private void RenderCurrentCard()
    {
        prevButton.IsEnabled = currentIndex > 0;
        nextButton.IsEnabled = currentIndex < cards.Count - 1;
        counterText.Text = cards.Count == 0 ? "-" : $"{currentIndex + 1} / {cards.Count}";

        if (cards.Count == 0)
        {
            currentTitleText.Text = string.Empty;
            previewCardView.Visibility = Visibility.Collapsed;
            panelView.SetOpen(false);
            return;
        }

        BoardCard card = cards[currentIndex];
        currentTitleText.Text = card.Title;
        previewCardView.Visibility = Visibility.Visible;
        previewCardView.Render(card, currentRendererRules);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (currentIndex <= 0)
        {
            return;
        }

        currentIndex -= 1;
        RenderCurrentCard();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (currentIndex >= cards.Count - 1)
        {
            return;
        }

        currentIndex += 1;
        RenderCurrentCard();
    }

    private static int ResolveNextIndex(string? previousCardId, IReadOnlyList<BoardCard> nextCards, int previousIndex)
    {
        if (nextCards.Count == 0)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(previousCardId))
        {
            for (int index = 0; index < nextCards.Count; index += 1)
            {
                if (string.Equals(nextCards[index].Id, previousCardId, System.StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return System.Math.Min(previousIndex, nextCards.Count - 1);
    }

    private static Style? ResolveStyle(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Style
            : null;
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }
}
