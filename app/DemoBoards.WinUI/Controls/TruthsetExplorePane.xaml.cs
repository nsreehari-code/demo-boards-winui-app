using DemoBoards.RuntimeHost;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class TruthsetExplorePane : UserControl
{
    private readonly PanelRail panelView;
    private readonly TextBlock countText;
    private readonly TextBlock currentTitleText;
    private readonly Border phaseBadge;
    private readonly TextBlock phaseText;
    private readonly Button prevButton;
    private readonly TextBlock counterText;
    private readonly Button nextButton;
    private readonly CardRenderer previewCard;

    private readonly List<BoardCard> cards = new();
    private int currentIndex;
    private IReadOnlyList<RendererRule>? currentRendererRules;

    public TruthsetExplorePane()
    {
        panelView = new PanelRail();
        panelView.Configure(new PanelRailOptions(
            Side: PanelRailSide.Right,
            ButtonPosition: FloatingButtonPosition.TopRight,
            PanelWidth: 352,
            OpenToolTipText: "Open Truthset Explorer",
            CloseToolTipText: "Close Truthset Explorer",
            OpenGlyph: "\uE76B",
            CloseGlyph: "\uE76C",
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
        phaseText = new TextBlock { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        phaseBadge = new Border
        {
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = phaseText,
        };
        prevButton = new Button { Content = "Prev", HorizontalAlignment = HorizontalAlignment.Right, Style = ResolveStyle("BoardToolbarButtonStyle") };
        prevButton.Click += OnPrevClick;
        counterText = new TextBlock { FontSize = 12, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.72 };
        nextButton = new Button { Content = "Next", HorizontalAlignment = HorizontalAlignment.Right, Style = ResolveStyle("BoardToolbarButtonStyle") };
        nextButton.Click += OnNextClick;
        previewCard = new CardRenderer();

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
            Text = "Truthset Explore",
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
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.Children.Add(currentTitleText);
        Grid.SetColumn(phaseBadge, 1);
        navGrid.Children.Add(phaseBadge);
        Grid.SetColumn(prevButton, 2);
        navGrid.Children.Add(prevButton);
        Grid.SetColumn(counterText, 3);
        navGrid.Children.Add(counterText);
        Grid.SetColumn(nextButton, 4);
        navGrid.Children.Add(nextButton);

        return new StackPanel
        {
            Spacing = 0,
            Children =
            {
                headerGrid,
                navGrid,
                previewCard,
            }
        };
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

    private void RenderCurrentCard()
    {
        prevButton.IsEnabled = currentIndex > 0;
        nextButton.IsEnabled = currentIndex < cards.Count - 1;
        counterText.Text = cards.Count == 0 ? "-" : $"{currentIndex + 1} / {cards.Count}";

        if (cards.Count == 0)
        {
            currentTitleText.Text = string.Empty;
            phaseText.Text = string.Empty;
            phaseBadge.Visibility = Visibility.Collapsed;
            previewCard.Visibility = Visibility.Collapsed;
            panelView.SetOpen(false);
            return;
        }

        BoardCard card = cards[currentIndex];
        currentTitleText.Text = card.Title;
        string phase = ResolvePhase(card);
        phaseText.Text = phase;
        phaseBadge.Visibility = Visibility.Visible;
        string tone = string.Equals(phase, "done", System.StringComparison.OrdinalIgnoreCase) ? "completed" : "fresh";
        phaseBadge.Background = CardShell.CreateToneBrush(tone, string.Equals(tone, "completed", System.StringComparison.Ordinal) ? (byte)0x44 : (byte)0x33);
        phaseBadge.BorderBrush = CardShell.CreateToneBrush(tone, string.Equals(tone, "completed", System.StringComparison.Ordinal) ? (byte)0x99 : (byte)0x88);
        phaseBadge.BorderThickness = new Thickness(1);
        previewCard.Visibility = Visibility.Visible;
        previewCard.Render(card, currentRendererRules);
    }

    private static string ResolvePhase(BoardCard card)
    {
        BoardCardField? phaseField = card.Fields.FirstOrDefault(field => string.Equals(field.Key, "phase", System.StringComparison.OrdinalIgnoreCase));
        if (phaseField is not null && !string.IsNullOrWhiteSpace(phaseField.Value))
        {
            return phaseField.Value;
        }

        return "active";
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
