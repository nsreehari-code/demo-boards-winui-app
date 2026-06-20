using System;
using System.Collections.Generic;
using DemoBoards.RuntimeHost;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DemoBoards_WinUI.Controls;

/// <summary>
/// Native board surface that re-instantiates the demo-boards-frontend card view
/// over the embedded runtime snapshot. Each card has a status-toned front and a
/// flippable runtime back, mirroring CardShell / CardBackface.
/// </summary>
public sealed partial class BoardCanvas : UserControl
{
    public BoardCanvas()
    {
        InitializeComponent();
    }

    public void Render(BoardSnapshot snapshot)
    {
        CardsHost.Children.Clear();

        if (snapshot is null || snapshot.CardCount == 0)
        {
            BoardTitleText.Text = "No board snapshot";
            BoardSummaryText.Text = "The embedded runtime has not published any cards yet.";
            return;
        }

        BoardTitleText.Text = snapshot.BoardId;
        BoardSummaryText.Text =
            $"{snapshot.CardCount} card(s)  •  Pending {snapshot.Pending}  •  In progress {snapshot.InProgress}  •  Completed {snapshot.Completed}  •  Failed {snapshot.Failed}";

        if (snapshot.Edges.Count > 0)
        {
            CardsHost.Children.Add(CreateDependencyTile(snapshot.Edges));
        }

        foreach (BoardCard card in snapshot.Cards)
        {
            CardsHost.Children.Add(CreateCardView(card));
        }
    }

    private static UIElement CreateDependencyTile(IReadOnlyList<BoardEdge> edges)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = "Dependencies",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{edges.Count} edge(s) across the board",
            FontSize = 12,
            Opacity = 0.6
        });

        foreach (BoardEdge edge in edges)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            row.Children.Add(new TextBlock { Text = edge.From, FontWeight = FontWeights.SemiBold, FontSize = 12 });
            row.Children.Add(new Border
            {
                Padding = new Thickness(6, 1, 6, 1),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x33, Colors.SteelBlue.R, Colors.SteelBlue.G, Colors.SteelBlue.B)),
                Child = new TextBlock { Text = "→ " + edge.Token + " →", FontSize = 12 }
            });
            row.Children.Add(new TextBlock { Text = edge.To, FontWeight = FontWeights.SemiBold, FontSize = 12 });
            stack.Children.Add(row);
        }

        return new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Colors.SteelBlue.R, Colors.SteelBlue.G, Colors.SteelBlue.B)),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
    }

    private static UIElement CreateCardView(BoardCard card)
    {
        var container = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = ToneBrush(card.Status, 0x66),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
        };

        var frontPanel = BuildFront(card);
        var backPanel = BuildBack(card);
        backPanel.Visibility = Visibility.Collapsed;

        var flipHost = new Grid();
        flipHost.Children.Add(frontPanel);
        flipHost.Children.Add(backPanel);

        AttachFlip(frontPanel, backPanel);
        container.Child = flipHost;
        return container;
    }

    private static FrameworkElement BuildFront(BoardCard card)
    {
        var stack = new StackPanel { Spacing = 8 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        header.Children.Add(BuildStatusPill(card.Status));
        header.Children.Add(new TextBlock
        {
            Text = card.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = card.Id,
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        if (card.Elements.Count > 0)
        {
            foreach (BoardCardElement element in card.Elements)
            {
                stack.Children.Add(BuildElement(element));
            }
        }
        else if (card.Fields.Count > 0)
        {
            stack.Children.Add(BuildFieldList(card.Fields));
        }

        if (card.Requires.Count > 0 || card.Provides.Count > 0)
        {
            stack.Children.Add(BuildTokenRow(card.Requires, card.Provides));
        }

        return WrapWithFlipButton(stack, "Details");
    }

    private static FrameworkElement BuildElement(BoardCardElement element)
    {
        return element.Kind switch
        {
            "metric" => BuildMetricElement(element),
            "list" => BuildListElement(element),
            "table" or "editable-table" => BuildTableElement(element),
            "badge" => BuildBadgeElement(element),
            "alert" => BuildAlertElement(element),
            "chart" => BuildChartElement(element),
            "markdown" or "markup" => BuildMarkdownElement(element),
            _ => BuildTextElement(element)
        };
    }

    private static FrameworkElement BuildElementLabel(string? label)
    {
        return new TextBlock
        {
            Text = label,
            FontSize = 12,
            Opacity = 0.6,
            FontWeight = FontWeights.SemiBold
        };
    }

    private static FrameworkElement BuildMetricElement(BoardCardElement element)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            stack.Children.Add(BuildElementLabel(element.Label));
        }

        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(element.Text) ? "—" : element.Text,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold
        });

        return stack;
    }

    private static FrameworkElement BuildListElement(BoardCardElement element)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            stack.Children.Add(BuildElementLabel(element.Label));
        }

        if (element.Items.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "No items.", Opacity = 0.6 });
        }
        else
        {
            foreach (string item in element.Items)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "• " + item,
                    TextWrapping = TextWrapping.WrapWholeWords
                });
            }
        }

        return stack;
    }

    private static FrameworkElement BuildTableElement(BoardCardElement element)
    {
        var stack = new StackPanel { Spacing = 4 };
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            stack.Children.Add(BuildElementLabel(element.Label));
        }

        if (element.Columns.Count == 0 || element.Rows.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "No data.", Opacity = 0.6 });
            return stack;
        }

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 2 };
        for (int column = 0; column < element.Columns.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < element.Columns.Count; column++)
        {
            var header = new TextBlock
            {
                Text = element.Columns[column],
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.7
            };
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, column);
            grid.Children.Add(header);
        }

        int maxRows = Math.Min(element.Rows.Count, 6);
        for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            IReadOnlyList<string> cells = element.Rows[rowIndex];
            for (int column = 0; column < element.Columns.Count && column < cells.Count; column++)
            {
                var cell = new TextBlock
                {
                    Text = cells[column],
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap
                };
                Grid.SetRow(cell, rowIndex + 1);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        stack.Children.Add(grid);
        return stack;
    }

    private static FrameworkElement BuildBadgeElement(BoardCardElement element)
    {
        return new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(0x33, Colors.SteelBlue.R, Colors.SteelBlue.G, Colors.SteelBlue.B)),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(element.Text) ? (element.Label ?? "badge") : element.Text,
                FontSize = 12
            }
        };
    }

    private static FrameworkElement BuildAlertElement(BoardCardElement element)
    {
        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, Colors.Goldenrod.R, Colors.Goldenrod.G, Colors.Goldenrod.B)),
            Background = new SolidColorBrush(Color.FromArgb(0x22, Colors.Goldenrod.R, Colors.Goldenrod.G, Colors.Goldenrod.B)),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(element.Text) ? (element.Label ?? "Alert") : element.Text,
                TextWrapping = TextWrapping.WrapWholeWords
            }
        };
    }

    private static FrameworkElement BuildTextElement(BoardCardElement element)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            stack.Children.Add(BuildElementLabel(element.Label));
        }

        stack.Children.Add(new TextBlock
        {
            Text = element.Text ?? string.Empty,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        return stack;
    }

    private static FrameworkElement BuildChartElement(BoardCardElement element)
    {
        var stack = new StackPanel { Spacing = 4 };
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            stack.Children.Add(BuildElementLabel(element.Label));
        }

        if (element.Points.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "No chart data.", Opacity = 0.6 });
            return stack;
        }

        double max = 0;
        foreach (BoardChartPoint point in element.Points)
        {
            if (point.Value > max) max = point.Value;
        }
        if (max <= 0) max = 1;

        int rendered = 0;
        foreach (BoardChartPoint point in element.Points)
        {
            if (rendered++ >= 8) break;

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = point.Label,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            var track = new Border
            {
                Height = 14,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(0x22, Colors.SteelBlue.R, Colors.SteelBlue.G, Colors.SteelBlue.B))
            };
            var bar = new Border
            {
                Height = 14,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(2, 180 * (point.Value / max)),
                Background = new SolidColorBrush(Color.FromArgb(0xAA, Colors.SteelBlue.R, Colors.SteelBlue.G, Colors.SteelBlue.B))
            };
            track.Child = bar;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var valueBlock = new TextBlock
            {
                Text = point.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 12,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueBlock, 2);
            row.Children.Add(valueBlock);

            stack.Children.Add(row);
        }

        return stack;
    }

    private static FrameworkElement BuildMarkdownElement(BoardCardElement element)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            stack.Children.Add(BuildElementLabel(element.Label));
        }

        string text = element.Text ?? string.Empty;
        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                stack.Children.Add(new TextBlock { Text = string.Empty, FontSize = 4 });
                continue;
            }

            stack.Children.Add(BuildMarkdownLine(line));
        }

        return stack;
    }

    private static TextBlock BuildMarkdownLine(string line)
    {
        string content = line;
        double fontSize = 14;
        Windows.UI.Text.FontWeight weight = FontWeights.Normal;
        var margin = new Thickness(0);

        if (content.StartsWith("### ", StringComparison.Ordinal))
        {
            content = content.Substring(4);
            fontSize = 14;
            weight = FontWeights.SemiBold;
        }
        else if (content.StartsWith("## ", StringComparison.Ordinal))
        {
            content = content.Substring(3);
            fontSize = 16;
            weight = FontWeights.SemiBold;
        }
        else if (content.StartsWith("# ", StringComparison.Ordinal))
        {
            content = content.Substring(2);
            fontSize = 18;
            weight = FontWeights.SemiBold;
        }
        else if (content.StartsWith("- ", StringComparison.Ordinal) || content.StartsWith("* ", StringComparison.Ordinal))
        {
            content = "•  " + content.Substring(2);
            margin = new Thickness(8, 0, 0, 0);
        }

        var block = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = weight,
            Margin = margin,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        AppendInlineMarkdown(block, content);
        return block;
    }

    private static void AppendInlineMarkdown(TextBlock block, string content)
    {
        // Minimal inline markdown: **bold** segments split out into bold runs.
        int index = 0;
        while (index < content.Length)
        {
            int open = content.IndexOf("**", index, StringComparison.Ordinal);
            if (open < 0)
            {
                block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = content.Substring(index) });
                break;
            }

            if (open > index)
            {
                block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = content.Substring(index, open - index) });
            }

            int close = content.IndexOf("**", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = content.Substring(open) });
                break;
            }

            block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = content.Substring(open + 2, close - open - 2),
                FontWeight = FontWeights.Bold
            });
            index = close + 2;
        }
    }

    private static FrameworkElement BuildBack(BoardCard card)
    {
        var stack = new StackPanel { Spacing = 8 };

        stack.Children.Add(new TextBlock
        {
            Text = "Runtime",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"Card: {card.Id}",
            Opacity = 0.75,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"Status: {card.Status}",
            Opacity = 0.75
        });

        if (!string.IsNullOrWhiteSpace(card.SchemaVersion))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Schema: {card.SchemaVersion}",
                Opacity = 0.75
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = "Computed values",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        stack.Children.Add(card.ComputedValues.Count == 0
            ? new TextBlock { Text = "No computed values.", Opacity = 0.6 }
            : BuildFieldList(card.ComputedValues));

        return WrapWithFlipButton(stack, "Back");
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

    private static FrameworkElement BuildTokenRow(IReadOnlyList<string> requires, IReadOnlyList<string> provides)
    {
        var stack = new StackPanel { Spacing = 4 };

        if (requires.Count > 0)
        {
            stack.Children.Add(BuildTokenChips("requires", requires, 0x33, Colors.SlateGray));
        }

        if (provides.Count > 0)
        {
            stack.Children.Add(BuildTokenChips("provides", provides, 0x33, Colors.SeaGreen));
        }

        return stack;
    }

    private static FrameworkElement BuildTokenChips(string label, IReadOnlyList<string> tokens, byte alpha, Color color)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });

        foreach (string token in tokens)
        {
            row.Children.Add(new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)),
                Child = new TextBlock { Text = token, FontSize = 12 }
            });
        }

        return row;
    }

    private static FrameworkElement BuildStatusPill(string status)
    {
        return new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ToneBrush(status, 0x33),
            Child = new TextBlock
            {
                Text = status,
                FontSize = 12,
                Foreground = ToneBrush(status, 0xFF)
            }
        };
    }

    private static FrameworkElement WrapWithFlipButton(FrameworkElement content, string buttonText)
    {
        var grid = new Grid { RowSpacing = 8 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(content, 0);
        grid.Children.Add(content);

        var button = new Button
        {
            Content = buttonText,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(button, 1);
        grid.Children.Add(button);

        grid.Tag = button;
        return grid;
    }

    private static void AttachFlip(FrameworkElement frontPanel, FrameworkElement backPanel)
    {
        if (frontPanel is Grid frontGrid && frontGrid.Tag is Button frontButton)
        {
            frontButton.Click += (_, _) =>
            {
                frontPanel.Visibility = Visibility.Collapsed;
                backPanel.Visibility = Visibility.Visible;
            };
        }

        if (backPanel is Grid backGrid && backGrid.Tag is Button backButton)
        {
            backButton.Click += (_, _) =>
            {
                backPanel.Visibility = Visibility.Collapsed;
                frontPanel.Visibility = Visibility.Visible;
            };
        }
    }

    private static Brush ToneBrush(string status, byte alpha)
    {
        Color color = (status ?? string.Empty).ToLowerInvariant() switch
        {
            "completed" => Colors.SeaGreen,
            "running" => Colors.SteelBlue,
            "in_progress" => Colors.SteelBlue,
            "failed" => Colors.IndianRed,
            "blocked" => Colors.Goldenrod,
            _ => Colors.SlateGray
        };

        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}
