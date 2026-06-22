using System.Collections.Generic;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class GandalfPane : UserControl
{
    private const string OpenGlyph = "\uE76C";
    private const string CloseGlyph = "\uE76B";

    private readonly FloatingCircleButton ToggleButton;
    private readonly Border PaneBorder;
    private readonly TextBlock CountText;
    private readonly TextBlock CurrentTitleText;
    private readonly Button PrevButton;
    private readonly TextBlock CounterText;
    private readonly Button NextButton;
    private readonly CardRenderer PreviewCardView;

    private readonly List<BoardCard> cards = new();
    private int currentIndex;
    private bool visible;

    public GandalfPane()
    {
        ToggleButton = new FloatingCircleButton
        {
            IconGlyph = OpenGlyph,
            FloatPosition = FloatingButtonPosition.TopLeft,
            Inset = 0,
            OffsetX = -10,
            OffsetY = 8,
            Diameter = 48,
            ButtonStyle = ResolveStyle("BoardEdgeToggleButtonStyle"),
            ActiveButtonStyle = ResolveStyle("BoardEdgeToggleButtonActiveStyle"),
            ToolTipText = "Open Board Manager",
        };
        ToggleButton.Click += OnToggleClick;

        CountText = new TextBlock { FontSize = 12, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
        CurrentTitleText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        PrevButton = new Button { Content = "Prev", HorizontalAlignment = HorizontalAlignment.Right, Style = ResolveStyle("BoardToolbarButtonStyle") };
        PrevButton.Click += OnPrevClick;
        CounterText = new TextBlock { FontSize = 12, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        NextButton = new Button { Content = "Next", HorizontalAlignment = HorizontalAlignment.Right, Style = ResolveStyle("BoardToolbarButtonStyle") };
        NextButton.Click += OnNextClick;
        PreviewCardView = new CardRenderer();

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
        Grid.SetColumn(CountText, 1);
        headerGrid.Children.Add(CountText);

        var navGrid = new Grid { Padding = new Thickness(16, 12, 16, 12), ColumnSpacing = 10 };
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.Children.Add(CurrentTitleText);
        Grid.SetColumn(PrevButton, 1);
        navGrid.Children.Add(PrevButton);
        Grid.SetColumn(CounterText, 2);
        navGrid.Children.Add(CounterText);
        Grid.SetColumn(NextButton, 3);
        navGrid.Children.Add(NextButton);

        PaneBorder = new Border
        {
            Margin = new Thickness(0, 56, 0, 0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed,
            Background = ResolveBrush("CardBackgroundFillColorDefaultBrush"),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    headerGrid,
                    navGrid,
                    PreviewCardView,
                }
            }
        };

        Content = new Grid
        {
            Children =
            {
                ToggleButton,
                PaneBorder,
            }
        };
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
        ToggleButton.IsActive = visible;
        ToggleButton.IconGlyph = visible ? CloseGlyph : OpenGlyph;
        ToggleButton.ToolTipText = visible ? "Close Board Manager" : "Open Board Manager";
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
