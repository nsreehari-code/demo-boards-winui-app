using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.State;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorInspectCardProps(BoardStore BoardStore, string CardId);

public sealed class ReactorInspectCardComponent : Component<ReactorInspectCardProps>
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    public override Element Render()
    {
        var (actionStatus, setActionStatus) = UseState(string.Empty);
        var (flightStatus, setFlightStatus) = UseState("Run the full card preflight or a source preflight to inspect live output here.");
        var (flightOutput, setFlightOutput) = UseState(string.Empty);
        var (deleteConfirmOpen, setDeleteConfirmOpen) = UseState(false);
        var (deletePending, setDeletePending) = UseState(false);
        var (_, setRevision) = UseState(string.Empty);

        UseEffect(() =>
        {
            EventHandler<BoardStoreChangedEventArgs> onStateChanged = (_, change) =>
            {
                if (change.ChangedCardIds.Contains(Props.CardId))
                {
                    setRevision(Guid.NewGuid().ToString("N"));
                }
            };

            Props.BoardStore.StateChanged += onStateChanged;
            return () => Props.BoardStore.StateChanged -= onStateChanged;
        }, Props.CardId);

        BoardCard? card = Props.BoardStore.GetCardDefinitionAndData(Props.CardId);
        if (card is null)
        {
            return TextBlock("Card not found.").Opacity(0.72);
        }

        string subtitle = $"{card.Id}  •  {NormalizeStatus(card.Status)}  •  Schema {ValueOrFallback(card.SchemaVersion, "n/a")}";
        string defaultActionStatus = BuildActionStatus(card);
        if (string.IsNullOrWhiteSpace(actionStatus))
        {
            setActionStatus(defaultActionStatus);
        }

        var leftColumn = new List<Element>
        {
            SectionCard("Summary",
                VStack(10,
                    TextBlock(card.Title).FontSize(20).Bold().Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBlock(subtitle).Opacity(0.72).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    BuildSummaryPillRow(new[]
                    {
                        ("Status", NormalizeStatus(card.Status), card.Status),
                        ("View kinds", card.ViewKinds.Count.ToString(), "fresh"),
                        ("Sources", card.SourceDefinitions.Count.ToString(), "fresh"),
                        ("Messages", card.ChatMessages.Count.ToString(), card.ChatProcessing ? "running" : "fresh")
                    }),
                    HStack(8,
                        Button("Refresh card", () => _ = RunActionAsync(
                            $"Refreshing {card.Id}...",
                            async () =>
                            {
                                await App.Current.BoardClient.RefreshCardAsync(card.Id);
                                return "Refresh dispatched.";
                            },
                            setActionStatus,
                            setFlightStatus,
                            setFlightOutput))
                            .AutomationName($"Refresh {card.Title}")
                            .SubtleButton(),
                        Button("Run full card preflight", () => _ = RunActionAsync(
                            "Running full card preflight...",
                            async () => await App.Current.BoardClient.CallBoardMcpAsync(
                                "preflight.run-one-cycle-with-candidate-card",
                                new
                                {
                                    candidate_card_content = card.RawDefinitionJson,
                                    mock_requires = BuildMockRequires(Props.BoardStore, card)
                                }),
                            setActionStatus,
                            setFlightStatus,
                            setFlightOutput))
                            .AutomationName($"Run preflight for {card.Title}")
                            .SubtleButton()),
                    Button(DeleteLabel(), () =>
                    {
                        if (!deletePending)
                        {
                            setDeleteConfirmOpen(true);
                        }
                    })
                        .AutomationName($"Delete {card.Title}")
                        .SubtleButton(),
                    TextBlock(string.IsNullOrWhiteSpace(actionStatus) ? defaultActionStatus : actionStatus)
                        .Opacity(0.8)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))),
            deleteConfirmOpen
                ? SectionCard("Confirm delete",
                    Component<ReactorChallengeConfirmModalComponent, ReactorChallengeConfirmModalProps>(
                        new ReactorChallengeConfirmModalProps(
                            $"This will remove card {card.Id} from the board runtime.",
                            () =>
                            {
                                if (!deletePending)
                                {
                                    _ = DeleteCardAsync(card.Id, setDeletePending, setDeleteConfirmOpen, setActionStatus, setFlightStatus, setFlightOutput, () => Props.BoardStore.SetInspectedCardId(null));
                                }
                            },
                            () =>
                            {
                                if (!deletePending)
                                {
                                    setDeleteConfirmOpen(false);
                                }
                            },
                            deletePending)))
                : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed),
            SectionCard("Preview",
                Component<ReactorCardRendererComponent, ReactorCardRendererProps>(new ReactorCardRendererProps(card, null))),
            SectionCard("Sources", BuildSources(card, setActionStatus, setFlightStatus, setFlightOutput)),
            SectionCard("Files", BuildFiles(card))
        };

        var rightColumn = new List<Element>
        {
            SectionCard("Backface", Component<ReactorCardBackfaceComponent, ReactorCardBackfaceProps>(new ReactorCardBackfaceProps(card))),
            SectionCard("Chat", Component<ReactorChatPaneComponent, ReactorChatPaneProps>(
                new ReactorChatPaneProps(Props.BoardStore, App.Current.BoardClient, card.Id, Compact: false, EnablePopout: false, Title: "Chat"))),
            SectionCard("Metadata", BuildFieldRows(card.MetaValues.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new BoardCardField(entry.Key, entry.Value)).ToArray(), "No metadata values on this card.")),
            SectionCard("Card Data", BuildFieldRows(card.Fields, "No card data fields exposed.")),
            SectionCard("Computed Values", BuildFieldRows(card.ComputedValues, "No computed values yet.")),
            SectionCard("Token Flows", BuildTokens(card, setFlightStatus, setFlightOutput)),
            SectionCard("Raw Definition", BuildCodeBlock(PrettyJson(card.RawDefinitionJson))),
            SectionCard("Raw Runtime", BuildCodeBlock(PrettyJson(card.RawRuntimeJson))),
            SectionCard("Live Action Output",
                VStack(8,
                    TextBlock(flightStatus).Opacity(0.72).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    BuildCodeBlock(string.IsNullOrWhiteSpace(flightOutput) ? "No output yet." : flightOutput)))
        };

        return HStack(16,
                ScrollViewer(VStack(12, leftColumn.ToArray()))
                    .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto)
                    .Width(380),
                ScrollViewer(VStack(12, rightColumn.ToArray()))
                    .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto)
                    .Flex(grow: 1));
    }

    private static Element BuildSources(BoardCard card, Action<string> setActionStatus, Action<string> setFlightStatus, Action<string> setFlightOutput)
    {
        if (card.SourceDefinitions.Count == 0)
        {
            return TextBlock("No source definitions on this card.").Opacity(0.62);
        }

        return VStack(8, card.SourceDefinitions.Select((source, index) =>
            (Element)Border(
                    VStack(6,
                        TextBlock($"Source {index + 1}: {ValueOrFallback(source.BindTo, "unbound")}").Bold().Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                        BuildFieldRows(source.DetailFields, string.Empty),
                        Button("Run Live Source", () => _ = RunActionAsync(
                            $"Running source {index + 1}...",
                            async () => await App.Current.BoardClient.CallBoardMcpAsync(
                                "preflight.run-single-source-in-live-card",
                                new
                                {
                                    card_id = card.Id,
                                    source_idx = index,
                                    mock_requires = BuildMockRequires(App.Current.BoardStore, card)
                                }),
                            setActionStatus,
                            setFlightStatus,
                            setFlightOutput))
                            .AutomationName($"Run live source {index + 1} for {card.Title}")
                            .SubtleButton()))
                .Padding(10)
                .WithBorder(CardToneBrushes.CreateToneBrush(card.Status, 0x38), 1)
                .CornerRadius(12)).ToArray());
    }

    private static Element BuildFiles(BoardCard card)
    {
        IReadOnlyList<FileRecord> files = ParseFiles(card.RawDefinitionJson);
        if (files.Count == 0)
        {
            return TextBlock("No stored files are attached to this card.").Opacity(0.62);
        }

        return VStack(8, files.Select((file, index) =>
            (Element)Border(
                    VStack(4,
                        TextBlock($"{index}: {ValueOrFallback(file.Name, ValueOrFallback(file.StoredName, "unnamed"))}").Bold().Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                        KeyValue("stored", ValueOrFallback(file.StoredName, "n/a")),
                        KeyValue("type", ValueOrFallback(file.MimeType, "n/a")),
                        KeyValue("size", file.Size.HasValue ? $"{file.Size.Value} bytes" : "unknown"),
                        string.IsNullOrWhiteSpace(file.UploadedAt) ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed) : KeyValue("uploaded", file.UploadedAt),
                        HyperlinkButton("Download file", Uri.TryCreate(App.Current.BoardClient.GetCardFileUrl(card.Id, index, file.StoredName), UriKind.Absolute, out Uri? uri) ? uri : null))
                .Padding(10)
                .Background(BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Microsoft.UI.Colors.White))
                .WithBorder(BoardTheme.ResolveBrush("BoardBorderBrush", Microsoft.UI.Colors.LightGray), 1)
                .CornerRadius(12))).ToArray());
    }

    private static Element BuildTokens(BoardCard card, Action<string> setFlightStatus, Action<string> setFlightOutput)
    {
        IReadOnlyDictionary<string, string> dataObjects = App.Current.BoardStore.GetRequiredDataObjects(card);
        return VStack(8,
            TextBlock("Requires").Bold().Opacity(0.8),
            BuildTokenList(card.Requires, dataObjects, "require", "No required tokens.", setFlightStatus, setFlightOutput),
            TextBlock("Provides").Bold().Opacity(0.8),
            BuildTokenList(card.Provides, dataObjects, "provide", "No provided tokens.", setFlightStatus, setFlightOutput));
    }

    private static Element BuildTokenList(IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string> dataObjects, string tokenKind, string emptyText, Action<string> setFlightStatus, Action<string> setFlightOutput)
    {
        if (tokens.Count == 0)
        {
            return TextBlock(emptyText).Opacity(0.62);
        }

        return VStack(8, tokens.Select(token =>
        {
            bool hasValue = dataObjects.TryGetValue(token, out string? value);
            return (Element)Button(
                    Border(
                        VStack(2,
                            TextBlock(token).Bold(),
                            TextBlock(hasValue ? value! : "No current value")
                                .Opacity(hasValue ? 0.84 : 0.58)
                                .Set(text =>
                                {
                                    text.TextWrapping = TextWrapping.WrapWholeWords;
                                    text.FontFamily = new FontFamily("Consolas");
                                }))
                        )
                        .Padding(8)
                        .Background(CardToneBrushes.CreateToneBrush(hasValue ? "completed" : "failed", 0x18))
                        .WithBorder(CardToneBrushes.CreateToneBrush(hasValue ? "completed" : "failed", 0x66), 1)
                        .CornerRadius(10),
                    () =>
                    {
                        setFlightStatus($"Selected {(tokenKind == "require" ? "required" : "provided")} token.");
                        setFlightOutput(PrettyJson(string.IsNullOrWhiteSpace(value) ? "null" : value!));
                    })
                .AutomationName($"Inspect token {token}")
                .SubtleButton();
        }).ToArray());
    }

    private static Element BuildFieldRows(IReadOnlyList<BoardCardField> fields, string emptyText)
    {
        if (fields.Count == 0)
        {
            return string.IsNullOrWhiteSpace(emptyText)
                ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                : TextBlock(emptyText).Opacity(0.62);
        }

        return VStack(6, fields.Select(field => (Element)KeyValue(field.Key, field.Value)).ToArray());
    }

    private static Element KeyValue(string key, string value)
    {
        return VStack(2,
            TextBlock(key).Bold().Opacity(0.78),
            TextBlock(ValueOrFallback(value, "n/a"))
                .Set(text =>
                {
                    text.TextWrapping = TextWrapping.WrapWholeWords;
                    text.FontFamily = new FontFamily("Consolas");
                    text.IsTextSelectionEnabled = true;
                }));
    }

    private static Element BuildCodeBlock(string value)
    {
        return TextBlock(value)
            .Set(text =>
            {
                text.TextWrapping = TextWrapping.WrapWholeWords;
                text.IsTextSelectionEnabled = true;
                text.FontFamily = new FontFamily("Consolas");
            });
    }

    private static Element SectionCard(string title, Element content)
    {
        return Border(
                VStack(8,
                    TextBlock(title).FontSize(18).Bold(),
                    content))
            .Padding(14)
            .Background(BoardTheme.ResolveBrush("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.White))
            .CornerRadius(14);
    }

    private static Element BuildSummaryPillRow(IEnumerable<(string Label, string Value, string Tone)> items)
    {
        return HStack(8, items.Select(item => (Element)Border(TextBlock($"{item.Label}: {item.Value}").FontSize(12).Bold())
            .Padding(10, 4, 10, 4)
            .Background(CardToneBrushes.CreateToneBrush(item.Tone, 0x18))
            .CornerRadius(12)).ToArray());
    }

    private static Element DeleteLabel()
    {
        return HStack(6,
            TextBlock("Delete").Opacity(0.72),
            TextBlock("Delete card"));
    }

    private static async System.Threading.Tasks.Task RunActionAsync(
        string pendingMessage,
        Func<System.Threading.Tasks.Task<string>> action,
        Action<string> setActionStatus,
        Action<string> setFlightStatus,
        Action<string> setFlightOutput,
        Action? onSuccess = null)
    {
        setActionStatus(pendingMessage);
        setFlightStatus(pendingMessage);
        setFlightOutput(string.Empty);
        try
        {
            string result = await action();
            setFlightStatus("Action completed.");
            setFlightOutput(PrettyJson(result));
            setActionStatus("Action completed.");
            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            setActionStatus(ex.Message);
            setFlightStatus("Action failed.");
            setFlightOutput(ex.ToString());
        }
    }

    private static async System.Threading.Tasks.Task DeleteCardAsync(
        string cardId,
        Action<bool> setDeletePending,
        Action<bool> setDeleteConfirmOpen,
        Action<string> setActionStatus,
        Action<string> setFlightStatus,
        Action<string> setFlightOutput,
        Action onSuccess)
    {
        setDeletePending(true);
        await RunActionAsync(
            $"Removing {cardId}...",
            async () => await App.Current.BoardClient.CallBoardMcpAsync("manage.remove-card", new { card_id = cardId }),
            setActionStatus,
            setFlightStatus,
            setFlightOutput,
            onSuccess: () =>
            {
                setDeleteConfirmOpen(false);
                onSuccess();
            });
        setDeletePending(false);
    }

    private static object BuildMockRequires(BoardStore boardStore, BoardCard card)
    {
        IReadOnlyDictionary<string, string> data = boardStore.GetRequiredDataObjects(card);
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
}