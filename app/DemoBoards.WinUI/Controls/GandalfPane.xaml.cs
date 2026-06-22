using System.Collections.Generic;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed partial class GandalfPane : UserControl
{
    private const string OpenGlyph = "\uE76C";
    private const string CloseGlyph = "\uE76B";

    private readonly List<BoardCard> cards = new();
    private int currentIndex;
    private bool visible;

    public GandalfPane()
    {
        InitializeComponent();
    }

    public void Render(IReadOnlyList<BoardCard> nextCards, IReadOnlyList<RendererRule>? rendererRules = null)
    {
        string? previousCardId = currentIndex >= 0 && currentIndex < cards.Count ? cards[currentIndex].Id : null;
        cards.Clear();
        if (nextCards is not null) cards.AddRange(nextCards);
        currentIndex = ResolveNextIndex(previousCardId, cards, currentIndex);
        currentRendererRules = rendererRules;

        CountText.Text = cards.Count == 0 ? "No matching cards" : $"{cards.Count} cards";
        ToggleButton.Visibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PaneBorder.Visibility = visible && cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleButtonState();
        RenderCurrentCard();
    }

    private void RenderCurrentCard()
    {
        PrevButton.IsEnabled = currentIndex > 0;
        NextButton.IsEnabled = currentIndex < cards.Count - 1;
        CounterText.Text = cards.Count == 0 ? "-" : $"{currentIndex + 1} / {cards.Count}";

        if (cards.Count == 0)
        {
            CurrentTitleText.Text = "";
            PreviewCardView.Visibility = Visibility.Collapsed;
            return;
        }

        BoardCard card = cards[currentIndex];
        CurrentTitleText.Text = card.Title;
        PreviewCardView.Visibility = Visibility.Visible;
        PreviewCardView.Render(card, currentRendererRules);
    }

    private IReadOnlyList<RendererRule>? currentRendererRules;

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        visible = !visible;
        PaneBorder.Visibility = visible && cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleButtonState();
    }

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (currentIndex <= 0) return;
        currentIndex -= 1;
        RenderCurrentCard();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (currentIndex >= cards.Count - 1) return;
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

    private void UpdateToggleButtonState()
    {
        ToggleButton.IconGlyph = visible ? CloseGlyph : OpenGlyph;
        ToggleButton.ToolTipText = visible ? "Close Board Manager" : "Open Board Manager";
    }
}
