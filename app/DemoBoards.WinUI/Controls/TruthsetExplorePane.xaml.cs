using DemoBoards.RuntimeHost;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed partial class TruthsetExplorePane : UserControl
{
    private const string OpenGlyph = "\uE76B";
    private const string CloseGlyph = "\uE76C";

    private readonly List<BoardCard> cards = new();
    private int currentIndex;
    private bool visible;
    private IReadOnlyList<RendererRule>? currentRendererRules;

    public TruthsetExplorePane()
    {
        InitializeComponent();
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
        CountText.Text = cards.Count == 0 ? "No matching cards" : $"{cards.Count} cards";
        ToggleButton.Visibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PaneBorder.Visibility = visible && cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleButtonState();
        RenderCurrentCard();
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        visible = !visible;
        PaneBorder.Visibility = visible && cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleButtonState();
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
        PrevButton.IsEnabled = currentIndex > 0;
        NextButton.IsEnabled = currentIndex < cards.Count - 1;
        CounterText.Text = cards.Count == 0 ? "-" : $"{currentIndex + 1} / {cards.Count}";

        if (cards.Count == 0)
        {
            CurrentTitleText.Text = string.Empty;
            PhaseText.Text = string.Empty;
            PhaseBadge.Visibility = Visibility.Collapsed;
            PreviewCard.Visibility = Visibility.Collapsed;
            return;
        }

        BoardCard card = cards[currentIndex];
        CurrentTitleText.Text = card.Title;
        string phase = ResolvePhase(card);
        PhaseText.Text = phase;
        PhaseBadge.Visibility = Visibility.Visible;
        string tone = string.Equals(phase, "done", System.StringComparison.OrdinalIgnoreCase) ? "completed" : "fresh";
        PhaseBadge.Background = CardShell.CreateToneBrush(tone, string.Equals(tone, "completed", System.StringComparison.Ordinal) ? (byte)0x44 : (byte)0x33);
        PhaseBadge.BorderBrush = CardShell.CreateToneBrush(tone, string.Equals(tone, "completed", System.StringComparison.Ordinal) ? (byte)0x99 : (byte)0x88);
        PhaseBadge.BorderThickness = new Thickness(1);
        PreviewCard.Visibility = Visibility.Visible;
        PreviewCard.Render(card, currentRendererRules);
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

    private void UpdateToggleButtonState()
    {
        ToggleButton.IconGlyph = visible ? CloseGlyph : OpenGlyph;
        ToggleButton.ToolTipText = visible ? "Close Truthset Explorer" : "Open Truthset Explorer";
    }
}
