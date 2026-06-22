using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed partial class CardBackface : UserControl
{
    private Action? runCardFlight;
    private Action<int, string?>? runSourceFlight;
    private Action<string, string>? inspectToken;
    private string activeTokenKey = string.Empty;
    private bool flightDisabled;
    private bool cardFlightLoading;
    private IReadOnlyDictionary<int, bool> loadingBySource = new Dictionary<int, bool>();

    public CardBackface()
    {
        InitializeComponent();
    }

    public void Render(
        BoardCard card,
        Action? onRunCardFlight = null,
        Action<int, string?>? onRunSourceFlight = null,
        Action<string, string>? onInspectToken = null,
        string? currentActiveTokenKey = null,
        bool disableFlights = false,
        bool isCardFlightLoading = false,
        IReadOnlyDictionary<int, bool>? loadingStatesBySource = null)
    {
        runCardFlight = onRunCardFlight;
        runSourceFlight = onRunSourceFlight;
        inspectToken = onInspectToken;
        activeTokenKey = currentActiveTokenKey ?? string.Empty;
        flightDisabled = disableFlights;
        cardFlightLoading = isCardFlightLoading;
        loadingBySource = loadingStatesBySource ?? new Dictionary<int, bool>();

        ContentHost.Children.Clear();

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(new TextBlock
        {
            Text = card.Id,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        if (runCardFlight is not null)
        {
            var runButton = new Button
            {
                Content = cardFlightLoading ? "Running..." : "Run card preflight",
                IsEnabled = !flightDisabled && !cardFlightLoading,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            runButton.Click += (_, _) => runCardFlight?.Invoke();
            Grid.SetColumn(runButton, 1);
            titleRow.Children.Add(runButton);
        }
        ContentHost.Children.Add(titleRow);

        AddInfoSection("Depends On", card.Requires, "require");
        AddInfoSection("Produces", card.Provides, "provide");
        AddSourceDefinitions(card.SourceDefinitions);
        AddInfoSection("Rendered Card Elements", card.ViewKinds);

        ContentHost.Children.Add(new TextBlock
        {
            Text = "Computed values",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        ContentHost.Children.Add(card.ComputedValues.Count == 0
            ? new TextBlock { Text = "No computed values.", Opacity = 0.6 }
            : BuildFieldList(card.ComputedValues));

        if (card.Requires.Count == 0
            && card.Provides.Count == 0
            && card.SourceDefinitions.Count == 0
            && card.ViewKinds.Count == 0)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = "No configuration found.",
                Opacity = 0.6,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
    }

    private void AddInfoSection(string title, IReadOnlyList<string> values, string? tokenKind = null)
    {
        if (values.Count == 0) return;

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (string value in values)
        {
            UIElement content = new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(8),
                Background = CardShell.CreateToneBrush(!string.IsNullOrWhiteSpace(tokenKind) && activeTokenKey == $"{tokenKind}:{value}" ? "running" : "fresh", 0x22),
                Child = new TextBlock { Text = value, FontSize = 12 }
            };

            if (!string.IsNullOrWhiteSpace(tokenKind) && inspectToken is not null)
            {
                var button = new Button
                {
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x00, 0, 0, 0)),
                    Content = content,
                    Tag = new TokenActionTag(value, tokenKind)
                };
                button.Click += OnTokenClick;
                wrap.Children.Add(button);
            }
            else
            {
                wrap.Children.Add(content);
            }
        }

        stack.Children.Add(wrap);
        ContentHost.Children.Add(stack);
    }

    private void OnTokenClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TokenActionTag tag)
        {
            return;
        }

        inspectToken?.Invoke(tag.Token, tag.Kind);
    }

    private void AddSourceDefinitions(IReadOnlyList<BoardSourceDefinition> sourceDefinitions)
    {
        if (sourceDefinitions.Count == 0) return;

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = "External Data", FontWeight = FontWeights.SemiBold });

        foreach ((BoardSourceDefinition sourceDefinition, int index) in sourceDefinitions.Select((source, idx) => (source, idx)))
        {
            var section = new StackPanel { Spacing = 4 };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(sourceDefinition.BindTo) ? "unbound" : sourceDefinition.BindTo,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            if (runSourceFlight is not null)
            {
                bool loading = loadingBySource.TryGetValue(index, out bool isLoading) && isLoading;
                var runButton = new Button
                {
                    Content = loading ? "Running..." : "Run source preflight",
                    IsEnabled = !flightDisabled && !loading,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                runButton.Click += (_, _) => runSourceFlight?.Invoke(index, sourceDefinition.BindTo);
                Grid.SetColumn(runButton, 1);
                header.Children.Add(runButton);
            }
            section.Children.Add(header);
            section.Children.Add(BuildFieldList(sourceDefinition.DetailFields));
            stack.Children.Add(section);
        }

        ContentHost.Children.Add(stack);
    }

    private static FrameworkElement BuildFieldList(IReadOnlyList<BoardCardField> fields)
    {
        var stack = new StackPanel { Spacing = 2 };
        foreach (BoardCardField field in fields)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            line.Children.Add(new TextBlock
            {
                Text = $"{field.Key}:",
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.8
            });
            line.Children.Add(new TextBlock
            {
                Text = field.Value,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            stack.Children.Add(line);
        }

        return stack;
    }

    private sealed record TokenActionTag(string Token, string Kind);
}
