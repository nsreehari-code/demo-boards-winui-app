using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed partial class InspectCard : UserControl
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    private BoardStore? boardStore;
    private EmbeddedBoardClient? boardClient;
    private string currentCardId = string.Empty;
    private BoardCard? currentCard;

    public InspectCard()
    {
        InitializeComponent();
        RefreshButton.Click += OnRefreshClick;
        RunCycleButton.Click += OnRunCycleClick;
        DeleteButton.Click += OnDeleteClick;
    }

    public void Render(BoardStore boardStore, string cardId)
    {
        this.boardStore = boardStore;
        boardClient = ((App)Application.Current).BoardClient;
        currentCardId = cardId;

        BoardCard? card = boardStore.GetCardDefinitionAndData(cardId);
        currentCard = card;
        if (card is null)
        {
            CardTitleText.Text = "Card not found";
            CardSubtitleText.Text = string.Empty;
            ActionStatusText.Text = string.Empty;
            FlightStatusText.Text = string.Empty;
            FlightSummaryHost.Children.Clear();
            FlightSectionsHost.Children.Clear();
            DefinitionText.Text = string.Empty;
            RuntimeText.Text = string.Empty;
            SummaryHost.Children.Clear();
            SourcesHost.Children.Clear();
            FilesHost.Children.Clear();
            MetadataHost.Children.Clear();
            FieldsHost.Children.Clear();
            ComputedHost.Children.Clear();
            RequiresHost.Children.Clear();
            ProvidesHost.Children.Clear();
            return;
        }

        CardTitleText.Text = card.Title;
        CardSubtitleText.Text = $"{card.Id}  •  {NormalizeStatus(card.Status)}  •  Schema {ValueOrFallback(card.SchemaVersion, "n/a")}";
        PreviewShell.Render(card);
        BackfaceView.Render(
            card,
            onRunCardFlight: () => _ = RunCardFlightAsync(),
            onRunSourceFlight: (index, bindTo) => _ = RunSourceFlight(index),
            onInspectToken: (token, kind) => RenderTokenOutput(token, kind, boardStore.GetRequiredDataObjects(card).TryGetValue(token, out string? tokenValue) ? tokenValue : null),
            disableFlights: false,
            isCardFlightLoading: false,
            loadingStatesBySource: new Dictionary<int, bool>());
        ChatPaneView.Bind(boardStore, ((App)Application.Current).BoardClient, cardId);
        DefinitionText.Text = PrettyJson(card.RawDefinitionJson);
        RuntimeText.Text = PrettyJson(card.RawRuntimeJson);
        ActionStatusText.Text = BuildActionStatus(card);
        RenderFlightEmpty();

        RenderSummary(card);
        RenderSources(card);
        RenderFiles(card);
        RenderMetadata(card);
        RenderFields(FieldsHost, card.Fields, "No card data fields exposed.");
        RenderFields(ComputedHost, card.ComputedValues, "No computed values yet.");
        RenderTokens(RequiresHost, card.Requires, boardStore.GetRequiredDataObjects(card), "require", "No required tokens.");
        RenderTokens(ProvidesHost, card.Provides, boardStore.GetRequiredDataObjects(card), "provide", "No provided tokens.");
        UpdateActionButtons(card);
    }

    private void UpdateActionButtons(BoardCard card)
    {
        bool canRefresh = boardStore?.GetCardState(card.Id)?.CanRefresh == true;
        RefreshButton.IsEnabled = canRefresh && !string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase);
        RunCycleButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
    }

    private void RenderSummary(BoardCard card)
    {
        SummaryHost.Children.Clear();
        SummaryHost.Children.Add(BuildSummaryPillRow(new[]
        {
            ("Status", NormalizeStatus(card.Status), card.Status),
            ("View kinds", card.ViewKinds.Count.ToString(), "fresh"),
            ("Sources", card.SourceDefinitions.Count.ToString(), "fresh"),
            ("Messages", card.ChatMessages.Count.ToString(), card.ChatProcessing ? "running" : "fresh")
        }));
    }

    private void RenderSources(BoardCard card)
    {
        SourcesHost.Children.Clear();
        if (card.SourceDefinitions.Count == 0)
        {
            SourcesHost.Children.Add(BuildEmptyState("No source definitions on this card."));
            return;
        }

        for (int index = 0; index < card.SourceDefinitions.Count; index++)
        {
            BoardSourceDefinition source = card.SourceDefinitions[index];
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new TextBlock
            {
                Text = $"Source {index + 1}: {ValueOrFallback(source.BindTo, "unbound")}",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords
            });

            if (source.DetailFields.Count > 0)
            {
                foreach (BoardCardField field in source.DetailFields)
                {
                    stack.Children.Add(BuildKeyValueRow(field.Key, field.Value));
                }
            }

            var runButton = new Button
            {
                Content = "Run Live Source",
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = index
            };
            runButton.Click += OnRunSourceClick;
            stack.Children.Add(runButton);

            SourcesHost.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = CardShell.CreateToneBrush(card.Status, 0x38),
                Child = stack
            });
        }
    }

    private void RenderFiles(BoardCard card)
    {
        FilesHost.Children.Clear();
        IReadOnlyList<FileRecord> files = ParseFiles(card.RawDefinitionJson);
        if (files.Count == 0)
        {
            FilesHost.Children.Add(BuildEmptyState("No stored files are attached to this card."));
            return;
        }

        for (int index = 0; index < files.Count; index++)
        {
            FileRecord file = files[index];
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = $"{index}: {ValueOrFallback(file.Name, ValueOrFallback(file.StoredName, "unnamed"))}",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            stack.Children.Add(BuildKeyValueRow("stored", ValueOrFallback(file.StoredName, "n/a")));
            stack.Children.Add(BuildKeyValueRow("type", ValueOrFallback(file.MimeType, "n/a")));
            stack.Children.Add(BuildKeyValueRow("size", file.Size.HasValue ? $"{file.Size.Value} bytes" : "unknown"));
            if (!string.IsNullOrWhiteSpace(file.UploadedAt))
            {
                stack.Children.Add(BuildKeyValueRow("uploaded", file.UploadedAt));
            }

            string href = ((App)Application.Current).BoardClient.GetCardFileUrl(card.Id, index, file.StoredName);
            stack.Children.Add(new HyperlinkButton
            {
                Content = "Download file",
                HorizontalAlignment = HorizontalAlignment.Left,
                NavigateUri = Uri.TryCreate(href, UriKind.Absolute, out Uri? uri) ? uri : null
            });

            FilesHost.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(12),
                Background = BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Colors.White),
                BorderBrush = BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray),
                BorderThickness = new Thickness(1),
                Child = stack
            });
        }
    }

    private void RenderMetadata(BoardCard card)
    {
        MetadataHost.Children.Clear();
        if (card.MetaValues.Count == 0)
        {
            MetadataHost.Children.Add(BuildEmptyState("No metadata values on this card."));
            return;
        }

        foreach (KeyValuePair<string, string> entry in card.MetaValues.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            MetadataHost.Children.Add(BuildKeyValueRow(entry.Key, entry.Value));
        }
    }

    private static void RenderFields(Panel host, IReadOnlyList<BoardCardField> fields, string emptyText)
    {
        host.Children.Clear();
        if (fields.Count == 0)
        {
            host.Children.Add(BuildEmptyState(emptyText));
            return;
        }

        foreach (BoardCardField field in fields)
        {
            host.Children.Add(BuildKeyValueRow(field.Key, field.Value));
        }
    }

    private void RenderTokens(Panel host, IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string> dataObjects, string tokenKind, string emptyText)
    {
        host.Children.Clear();
        if (tokens.Count == 0)
        {
            host.Children.Add(BuildEmptyState(emptyText));
            return;
        }

        foreach (string token in tokens)
        {
            bool hasValue = dataObjects.TryGetValue(token, out string? value);
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = token,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = hasValue ? value : "No current value",
                Opacity = hasValue ? 0.84 : 0.58,
                TextWrapping = TextWrapping.WrapWholeWords,
                FontFamily = new FontFamily("Consolas")
            });
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new Border
                {
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(10),
                    Background = CardShell.CreateToneBrush(hasValue ? "completed" : "failed", 0x18),
                    BorderBrush = CardShell.CreateToneBrush(hasValue ? "completed" : "failed", 0x66),
                    BorderThickness = new Thickness(1),
                    Child = stack
                },
                Tag = new TokenInspectTag(token, tokenKind, hasValue ? value : null)
            };
            button.Click += OnTokenInspectClick;
            host.Children.Add(button);
        }
    }

    private void OnTokenInspectClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TokenInspectTag token)
        {
            return;
        }

        RenderTokenOutput(token.Token, token.TokenKind, token.Value);
    }

    private void RenderFlightEmpty()
    {
        FlightSummaryHost.Children.Clear();
        FlightSectionsHost.Children.Clear();
        FlightStatusText.Text = "Run the full card preflight or a source preflight to inspect live output here.";
    }

    private void RenderFlightPending(string title)
    {
        FlightSummaryHost.Children.Clear();
        FlightSectionsHost.Children.Clear();
        FlightStatusText.Text = title;
        FlightSummaryHost.Children.Add(BuildSummaryPill("State", "running", "running"));
    }

    private void RenderFlightError(string message)
    {
        FlightSummaryHost.Children.Clear();
        FlightSectionsHost.Children.Clear();
        FlightStatusText.Text = string.IsNullOrWhiteSpace(message) ? "Action failed." : message;
        FlightSummaryHost.Children.Add(BuildSummaryPill("State", "failed", "failed"));
    }

    private void RenderTokenOutput(string token, string tokenKind, string? value)
    {
        FlightSummaryHost.Children.Clear();
        FlightSectionsHost.Children.Clear();
        FlightStatusText.Text = $"Selected {(tokenKind == "require" ? "required" : "provided")} token.";
        FlightSummaryHost.Children.Add(BuildSummaryPill("Token", token, tokenKind == "require" ? "running" : "fresh"));
        FlightSummaryHost.Children.Add(BuildSummaryPill("State", string.IsNullOrWhiteSpace(value) ? "missing" : "available", string.IsNullOrWhiteSpace(value) ? "failed" : "fresh"));
        FlightSectionsHost.Children.Add(BuildJsonSection("Current Data", PrettyJson(string.IsNullOrWhiteSpace(value) ? "null" : value)));
    }

    private void RenderFlightResult(string payload)
    {
        FlightSummaryHost.Children.Clear();
        FlightSectionsHost.Children.Clear();

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            JsonElement data = root.TryGetProperty("status", out JsonElement statusElement)
                && statusElement.ValueKind == JsonValueKind.String
                && string.Equals(statusElement.GetString(), "success", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("data", out JsonElement nestedData)
                ? nestedData
                : root;

            if (data.ValueKind != JsonValueKind.Object)
            {
                FlightStatusText.Text = "Action completed.";
                FlightSectionsHost.Children.Add(BuildJsonSection("Payload", PrettyJson(payload)));
                return;
            }

            bool ok = !data.TryGetProperty("ok", out JsonElement okElement) || okElement.ValueKind != JsonValueKind.False;
            string stateLabel = ok ? "completed" : "failed";
            string tone = ok ? "fresh" : "failed";
            FlightSummaryHost.Children.Add(BuildSummaryPill("State", stateLabel, tone));

            if (data.TryGetProperty("cardId", out JsonElement cardIdElement) && cardIdElement.ValueKind == JsonValueKind.String)
            {
                string cardId = cardIdElement.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    FlightSummaryHost.Children.Add(BuildSummaryPill("Card", cardId, "fresh"));
                }
            }

            if (data.TryGetProperty("bindTo", out JsonElement bindToElement) && bindToElement.ValueKind == JsonValueKind.String)
            {
                string bindTo = bindToElement.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(bindTo))
                {
                    FlightSummaryHost.Children.Add(BuildSummaryPill("Source", bindTo, "fresh"));
                }
            }

            if (data.TryGetProperty("issues", out JsonElement issuesElement) && issuesElement.ValueKind == JsonValueKind.Array)
            {
                string[] issues = issuesElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (issues.Length > 0)
                {
                    FlightSummaryHost.Children.Add(BuildSummaryPill("Issues", issues.Length.ToString(), issues.Length > 0 ? "failed" : "fresh"));
                    FlightSectionsHost.Children.Add(BuildListSection("Issues", issues));
                }
            }

            if (data.TryGetProperty("provides_outputs", out JsonElement providesElement))
            {
                FlightSectionsHost.Children.Add(BuildJsonSection("Provides Outputs", PrettyJson(providesElement.GetRawText())));
            }

            if (data.TryGetProperty("rendered_view", out JsonElement renderedViewElement))
            {
                FlightSectionsHost.Children.Add(BuildJsonSection("Rendered View", PrettyJson(renderedViewElement.GetRawText())));
            }

            if (data.TryGetProperty("result", out JsonElement resultElement))
            {
                FlightSectionsHost.Children.Add(BuildJsonSection("Result", PrettyJson(resultElement.GetRawText())));
            }

            if (FlightSectionsHost.Children.Count == 0)
            {
                FlightSectionsHost.Children.Add(BuildJsonSection("Payload", PrettyJson(data.GetRawText())));
            }

            FlightStatusText.Text = "Action completed.";
        }
        catch
        {
            FlightStatusText.Text = "Action completed.";
            FlightSectionsHost.Children.Add(BuildJsonSection("Payload", PrettyJson(payload)));
        }
    }

    private static Border BuildJsonSection(string title, string value)
    {
        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            Background = CardShell.CreateToneBrush("fresh", 0x10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = value,
                        FontFamily = new FontFamily("Consolas"),
                        IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.WrapWholeWords
                    }
                }
            }
        };
    }

    private static Border BuildListSection(string title, IReadOnlyList<string> items)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold
        });
        foreach (string item in items)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"- {item}",
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            Background = CardShell.CreateToneBrush("failed", 0x10),
            Child = stack
        };
    }

    private static Border BuildSummaryPill(string label, string value, string tone)
    {
        return new Border
        {
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(12),
            Background = CardShell.CreateToneBrush(tone, 0x18),
            Child = new TextBlock
            {
                Text = $"{label}: {value}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        await RunActionAsync(
            $"Refreshing {currentCardId}...",
            async () =>
            {
                await boardClient.RefreshCardAsync(currentCardId);
                return "Refresh dispatched.";
            });
    }

    private async void OnRunCycleClick(object sender, RoutedEventArgs e)
    {
        await RunCardFlightAsync();
    }

    private async System.Threading.Tasks.Task RunCardFlightAsync()
    {
        if (boardClient is null || currentCard is null)
        {
            return;
        }

        await RunActionAsync(
            "Running full card preflight...",
            async () => await boardClient.CallBoardMcpAsync(
                "preflight.run-one-cycle-with-candidate-card",
                new
                {
                    candidate_card_content = currentCard.RawDefinitionJson,
                    mock_requires = BuildMockRequires(currentCard)
                }));
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        await RunActionAsync(
            $"Removing {currentCardId}...",
            async () => await boardClient.CallBoardMcpAsync("manage.remove-card", new { card_id = currentCardId }),
            onSuccess: () => boardStore?.SetInspectedCardId(null));
    }

    private async void OnRunSourceClick(object sender, RoutedEventArgs e)
    {
        if (boardClient is null || currentCard is null || sender is not Button button || button.Tag is not int sourceIndex)
        {
            return;
        }

        await RunSourceFlight(sourceIndex);
    }

    private async System.Threading.Tasks.Task RunSourceFlight(int sourceIndex)
    {
        if (boardClient is null || currentCard is null)
        {
            return;
        }

        await RunActionAsync(
            $"Running source {sourceIndex + 1}...",
            async () => await boardClient.CallBoardMcpAsync(
                "preflight.run-single-source-in-live-card",
                new
                {
                    card_id = currentCard.Id,
                    source_idx = sourceIndex,
                    mock_requires = BuildMockRequires(currentCard)
                }));
    }

    private async System.Threading.Tasks.Task RunActionAsync(string pendingMessage, Func<System.Threading.Tasks.Task<string>> action, Action? onSuccess = null)
    {
        ActionStatusText.Text = pendingMessage;
        RenderFlightPending(pendingMessage);
        try
        {
            string result = await action();
            RenderFlightResult(result);
            ActionStatusText.Text = "Action completed.";
            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = ex.Message;
            RenderFlightError(ex.Message);
        }
    }

    private object BuildMockRequires(BoardCard card)
    {
        IReadOnlyDictionary<string, string> data = boardStore?.GetRequiredDataObjects(card)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in data)
        {
            result[entry.Key] = ParseLooseValue(entry.Value);
        }

        return result;
    }

    private static object? ParseLooseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<object>(value);
        }
        catch
        {
            return value;
        }
    }

    private static string BuildActionStatus(BoardCard card)
    {
        if (card.ChatProcessing)
        {
            return "Card chat processing is active. Inspect flights will reflect the next published runtime state.";
        }

        if (string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase))
        {
            return "Card runtime is currently running.";
        }

        return "Use the live-source and candidate-cycle actions to compare runtime behavior without leaving the inspect surface.";
    }

    private static StackPanel BuildSummaryPillRow(IEnumerable<(string Label, string Value, string Tone)> items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach ((string label, string value, string tone) in items)
        {
            row.Children.Add(new Border
            {
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(12),
                Background = CardShell.CreateToneBrush(tone, 0x18),
                Child = new TextBlock
                {
                    Text = $"{label}: {value}",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            });
        }

        return row;
    }

    private static FrameworkElement BuildKeyValueRow(string key, string value)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = key,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.78
        });
        stack.Children.Add(new TextBlock
        {
            Text = ValueOrFallback(value, "n/a"),
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Consolas")
        });
        return stack;
    }

    private static TextBlock BuildEmptyState(string text)
    {
        return new TextBlock
        {
            Text = text,
            Opacity = 0.62,
            TextWrapping = TextWrapping.WrapWholeWords
        };
    }

    private static string PrettyJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
        }
        catch
        {
            return raw;
        }
    }

    private static string NormalizeStatus(string status)
    {
        string normalized = (status ?? string.Empty).Trim();
        return normalized.Length == 0 ? "fresh" : normalized.Replace('_', ' ');
    }

    private static string ValueOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static IReadOnlyList<FileRecord> ParseFiles(string rawDefinitionJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawDefinitionJson);
            if (!document.RootElement.TryGetProperty("card_data", out JsonElement cardData)
                || cardData.ValueKind != JsonValueKind.Object
                || !cardData.TryGetProperty("files", out JsonElement files)
                || files.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<FileRecord>();
            }

            return files.EnumerateArray()
                .Select(file => new FileRecord(
                    file.TryGetProperty("name", out JsonElement name) ? name.GetString() : null,
                    file.TryGetProperty("stored_name", out JsonElement storedName) ? storedName.GetString() : null,
                    file.TryGetProperty("mime_type", out JsonElement mimeType) ? mimeType.GetString() : null,
                    file.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long parsedSize) ? parsedSize : null,
                    file.TryGetProperty("uploaded_at", out JsonElement uploadedAt) ? uploadedAt.GetString() : null))
                .ToArray();
        }
        catch
        {
            return Array.Empty<FileRecord>();
        }
    }

    private sealed record FileRecord(string? Name, string? StoredName, string? MimeType, long? Size, string? UploadedAt);

    private sealed record TokenInspectTag(string Token, string TokenKind, string? Value);
}
