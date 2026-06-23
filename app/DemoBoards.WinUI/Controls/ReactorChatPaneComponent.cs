using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.State;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorChatPaneProps(
    BoardStore BoardStore,
    EmbeddedBoardClient BoardClient,
    string CardId,
    bool Compact,
    bool EnablePopout,
    string Title);

public sealed class ReactorChatPaneComponent : Component<ReactorChatPaneProps>
{
    private const string AgentOutputChannel = "agent-output";
    private const string AgentToolsChannel = "agent-tools";
    private const int HistoryTurnsPerPage = 5;

    public override Element Render()
    {
        var (_, setRevision) = UseState(string.Empty);
        var (composerText, setComposerText) = UseState(string.Empty);
        var (sending, setSending) = UseState(false);
        var (historyLoading, setHistoryLoading) = UseState(false);
        var (historyMessages, setHistoryMessages) = UseState<IReadOnlyList<BoardChatMessage>>(Array.Empty<BoardChatMessage>());
        var (historyAnchorTurnId, setHistoryAnchorTurnId) = UseState(string.Empty);
        var (historyCursorTurnId, setHistoryCursorTurnId) = UseState(string.Empty);
        var (hasMoreHistory, setHasMoreHistory) = UseState(false);
        var (statusText, setStatusText) = UseState(string.Empty);
        var (draftTurnId, setDraftTurnId) = UseState(CreateTurnId());
        var (draftAttachmentNames, setDraftAttachmentNames) = UseState<IReadOnlyList<string>>(Array.Empty<string>());
        var subscribedRef = UseRef(false);

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

        UseEffect(() =>
        {
            _ = EnsureSubscriptionsAsync(subscribedRef, Props.BoardClient, Props.CardId);
            return () =>
            {
                _ = DisposeSubscriptionsAsync(subscribedRef, Props.BoardClient, Props.CardId);
            };
        }, Props.CardId);

        BoardCard? card = Props.BoardStore.GetCardDefinitionAndData(Props.CardId);
        BoardWatchpartyState watchparty = Props.BoardStore.GetCardWatchparty(Props.CardId);

        UseEffect(() =>
        {
            if (card is null || !string.IsNullOrWhiteSpace(historyAnchorTurnId))
            {
                return;
            }

            string firstTurn = card.ChatMessages
                .Select(message => message.Turn?.Trim() ?? string.Empty)
                .FirstOrDefault(turn => !string.IsNullOrWhiteSpace(turn))
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(firstTurn))
            {
                return;
            }

            setHistoryAnchorTurnId(firstTurn);
            setHistoryCursorTurnId(firstTurn);
            setHasMoreHistory(true);
            _ = LoadHistoryBeforeAsync(
                Props.BoardClient,
                Props.CardId,
                firstTurn,
                historyMessages,
                setHistoryLoading,
                setHistoryMessages,
                setHistoryCursorTurnId,
                setHasMoreHistory);
        }, card?.ChatMessages.Count ?? 0, historyAnchorTurnId);

        if (card is null)
        {
            return TextBlock("Card not found.").Opacity(0.72);
        }

        string liveStatus = card.ChatProcessing
            ? "Agent is processing the current turn."
            : card.ChatReceiving
                ? "Waiting for more streamed chat output."
                : sending
                    ? "Sending chat message..."
                    : draftAttachmentNames.Count > 0
                        ? $"{draftAttachmentNames.Count} attachment(s) staged for the next send."
                        : string.IsNullOrWhiteSpace(statusText)
                            ? "Ready to send a message."
                            : statusText;

        var messages = new List<Element>();
        if (hasMoreHistory)
        {
            messages.Add(Button(historyLoading ? "Loading previous messages..." : "Show previous messages", () =>
            {
                if (historyLoading || string.IsNullOrWhiteSpace(historyCursorTurnId))
                {
                    return;
                }

                _ = LoadHistoryBeforeAsync(
                    Props.BoardClient,
                    Props.CardId,
                    historyCursorTurnId,
                    historyMessages,
                    setHistoryLoading,
                    setHistoryMessages,
                    setHistoryCursorTurnId,
                    setHasMoreHistory);
            })
                .AutomationName("Load previous chat messages")
                .SubtleButton());
        }

        messages.AddRange(historyMessages.Select(BuildMessage));

        if (card.ChatMessages.Count == 0)
        {
            messages.Add(TextBlock("No chat messages on this card yet.").Opacity(0.6));
        }
        else
        {
            messages.AddRange(card.ChatMessages.Select(BuildMessage));
        }

        Element? workingBubble = BuildWorkingBubble(card, watchparty);
        if (workingBubble is not null)
        {
            messages.Add(workingBubble);
        }

        if (draftAttachmentNames.Count > 0)
        {
            messages.AddRange(draftAttachmentNames.Select(name =>
                (Element)Border(TextBlock(name).FontSize(12).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))
                    .Padding(8, 4, 8, 4)
                    .Background(CardToneBrushes.CreateToneBrush("running", 0x12))
                    .CornerRadius(10)));
        }

        var root = new List<Element>();
        if (Props.Compact)
        {
            root.Add(HStack(8,
                TextBlock(Props.Title).Bold().Flex(grow: 1),
                Button(IconLabel(HostIconSources.ChatPopout, "Pop out"), () => ReactorShellBridge.RequestChatPopout(Props.CardId, Props.Title))
                    .AutomationName($"Pop out chat for {Props.Title}")
                    .SubtleButton()
                    .Set(button => button.Visibility = Props.EnablePopout ? Visibility.Visible : Visibility.Collapsed)));
        }

        root.Add(ScrollViewer(VStack(10, messages.ToArray()))
            .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto)
            .MinHeight(Props.Compact ? 180 : 280));

        if (!Props.Compact)
        {
            root.Add(VStack(6,
                TextBox(composerText, setComposerText)
                    .AutomationName($"Chat message input for {Props.Title}")
                    .AcceptsReturn(true)
                    .TextWrapping(TextWrapping.Wrap)
                    .PlaceholderText("Type a message")
                    .MinHeight(84),
                HStack(8,
                    TextBlock(liveStatus).Opacity(0.72).Flex(grow: 1).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    Button(IconLabel(HostIconSources.ChatAttach, "Attach File"), () =>
                    {
                        if (sending)
                        {
                            return;
                        }

                        _ = AttachFileAsync(Props.BoardClient, Props.CardId, draftTurnId, draftAttachmentNames, setDraftAttachmentNames, setStatusText, setSending);
                    })
                        .AutomationName("Attach file to chat")
                        .SubtleButton(),
                    Button(sending ? "Sending..." : "Send", () =>
                    {
                        if (sending || string.IsNullOrWhiteSpace(composerText))
                        {
                            return;
                        }

                        _ = SendAsync(Props.BoardClient, Props.CardId, composerText, draftTurnId, setComposerText, setSending, setStatusText, setDraftAttachmentNames, setDraftTurnId);
                    })
                        .AutomationName("Send chat message")
                        .AccentButton())));
        }
        else
        {
            root.Add(HStack(8,
                TextBlock(liveStatus).Opacity(0.72).Flex(grow: 1).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                Button("Send", () => ReactorShellBridge.RequestChatPopout(Props.CardId, Props.Title))
                    .AutomationName($"Open full chat for {Props.Title}")
                    .SubtleButton()));
        }

        return VStack(10, root.ToArray());
    }

    private static async Task EnsureSubscriptionsAsync(Ref<bool> subscribedRef, EmbeddedBoardClient boardClient, string cardId)
    {
        if (subscribedRef.Current || string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        subscribedRef.Current = true;
        try
        {
            await boardClient.SubscribeCardChatsAsync(cardId);
            await boardClient.SubscribeWatchpartyAsync(cardId, AgentOutputChannel);
            await boardClient.SubscribeWatchpartyAsync(cardId, AgentToolsChannel);
        }
        catch
        {
        }
    }

    private static async Task DisposeSubscriptionsAsync(Ref<bool> subscribedRef, EmbeddedBoardClient boardClient, string cardId)
    {
        if (!subscribedRef.Current || string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        try
        {
            await boardClient.UnsubscribeWatchpartyAsync(cardId, AgentToolsChannel);
            await boardClient.UnsubscribeWatchpartyAsync(cardId, AgentOutputChannel);
            await boardClient.UnsubscribeCardChatsAsync(cardId);
        }
        catch
        {
        }
        finally
        {
            subscribedRef.Current = false;
        }
    }

    private static async Task SendAsync(
        EmbeddedBoardClient boardClient,
        string cardId,
        string composerText,
        string draftTurnId,
        Action<string> setComposerText,
        Action<bool> setSending,
        Action<string> setStatusText,
        Action<IReadOnlyList<string>> setDraftAttachmentNames,
        Action<string> setDraftTurnId)
    {
        setSending(true);
        setStatusText("Sending chat message...");
        try
        {
            await boardClient.SendChatAsync(cardId, composerText.Trim(), draftTurnId);
            setComposerText(string.Empty);
            setDraftAttachmentNames(Array.Empty<string>());
            setDraftTurnId(CreateTurnId());
        }
        catch (Exception ex)
        {
            setStatusText($"Chat send failed: {ex.Message}");
        }
        finally
        {
            setSending(false);
        }
    }

    private static async Task AttachFileAsync(
        EmbeddedBoardClient boardClient,
        string cardId,
        string draftTurnId,
        IReadOnlyList<string> draftAttachmentNames,
        Action<IReadOnlyList<string>> setDraftAttachmentNames,
        Action<string> setStatusText,
        Action<bool> setSending)
    {
        try
        {
            NativeAttachmentFile? file = await NativeFilePicker.PickSingleAttachmentAsync();
            if (file is null)
            {
                return;
            }

            setSending(true);
            setStatusText($"Uploading {file.Name}...");
            await boardClient.AddChatAttachmentAsync(cardId, draftTurnId, file);
            setDraftAttachmentNames(draftAttachmentNames.Concat(new[] { file.Name }).ToArray());
            setStatusText($"Attached {file.Name}.");
        }
        catch (Exception ex)
        {
            setStatusText($"Attachment upload failed: {ex.Message}");
        }
        finally
        {
            setSending(false);
        }
    }

    private static async Task LoadHistoryBeforeAsync(
        EmbeddedBoardClient boardClient,
        string cardId,
        string beforeTurnId,
        IReadOnlyList<BoardChatMessage> existingHistory,
        Action<bool> setHistoryLoading,
        Action<IReadOnlyList<BoardChatMessage>> setHistoryMessages,
        Action<string> setHistoryCursorTurnId,
        Action<bool> setHasMoreHistory)
    {
        if (string.IsNullOrWhiteSpace(beforeTurnId) || string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        setHistoryLoading(true);
        try
        {
            string payload = await boardClient.CallBoardMcpAsync(
                "inspect.chat-messages-on-cards",
                new { card_id = cardId, tail_turns = HistoryTurnsPerPage, tail_turns_before_id = beforeTurnId });
            IReadOnlyList<BoardChatMessage> older = ParseChatMessages(payload);
            if (older.Count == 0)
            {
                setHasMoreHistory(false);
                return;
            }

            setHistoryMessages(MergeHistoryMessages(older, existingHistory));
            string nextCursor = older
                .Select(message => message.Turn?.Trim() ?? string.Empty)
                .FirstOrDefault(turn => !string.IsNullOrWhiteSpace(turn))
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nextCursor) || string.Equals(nextCursor, beforeTurnId, StringComparison.Ordinal))
            {
                setHasMoreHistory(false);
            }
            else
            {
                setHistoryCursorTurnId(nextCursor);
                setHasMoreHistory(CountDistinctTurns(older) >= HistoryTurnsPerPage);
            }
        }
        catch
        {
            setHasMoreHistory(false);
        }
        finally
        {
            setHistoryLoading(false);
        }
    }

    private static IReadOnlyList<BoardChatMessage> MergeHistoryMessages(IReadOnlyList<BoardChatMessage> older, IReadOnlyList<BoardChatMessage> existing)
    {
        var seeded = older.ToList();
        seeded.AddRange(existing);
        return seeded
            .GroupBy(message => $"{message.Turn}|{message.Role}|{message.Text}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static int CountDistinctTurns(IReadOnlyList<BoardChatMessage> messages)
    {
        return messages
            .Select(message => message.Turn?.Trim() ?? string.Empty)
            .Where(turn => !string.IsNullOrWhiteSpace(turn))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static IReadOnlyList<BoardChatMessage> ParseChatMessages(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        JsonElement data = root.TryGetProperty("status", out JsonElement status)
            && status.ValueKind == JsonValueKind.String
            && string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("data", out JsonElement nestedData)
            ? nestedData
            : root;

        if (!data.TryGetProperty("messages", out JsonElement messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BoardChatMessage>();
        }

        return messages.EnumerateArray()
            .Select(ParseChatMessage)
            .Where(message => message is not null)
            .Cast<BoardChatMessage>()
            .ToArray();
    }

    private static BoardChatMessage? ParseChatMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string role = message.TryGetProperty("role", out JsonElement roleElement) && roleElement.ValueKind == JsonValueKind.String
            ? roleElement.GetString() ?? string.Empty
            : string.Empty;
        string text = message.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        string turn = message.TryGetProperty("turn", out JsonElement turnElement) && turnElement.ValueKind == JsonValueKind.String
            ? turnElement.GetString() ?? string.Empty
            : string.Empty;
        bool processing = message.TryGetProperty("processing", out JsonElement processingElement)
            && processingElement.ValueKind == JsonValueKind.True;
        return new BoardChatMessage(role, text, turn, processing);
    }

    private static Element BuildMessage(BoardChatMessage message)
    {
        return Border(
                VStack(4,
                    TextBlock(string.IsNullOrWhiteSpace(message.Turn) ? message.Role : $"{message.Role} • {message.Turn}")
                        .FontSize(12)
                        .Opacity(0.72)
                        .Bold(),
                    TextBlock(message.Text).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)))
            .Padding(10)
            .Background(message.Role == "user"
                ? CardToneBrushes.CreateToneBrush("running", 0x16)
                : CardToneBrushes.CreateToneBrush(message.Processing ? "running" : "fresh", 0x12))
            .CornerRadius(12);
    }

    private static Element? BuildWorkingBubble(BoardCard card, BoardWatchpartyState watchparty)
    {
        bool show = card.ChatProcessing
            || card.ChatReceiving
            || !string.IsNullOrWhiteSpace(watchparty.AgentOutput)
            || !string.IsNullOrWhiteSpace(watchparty.AgentTools);
        if (!show)
        {
            return null;
        }

        var details = new List<Element>();
        if (!string.IsNullOrWhiteSpace(watchparty.AgentOutput))
        {
            details.Add(BuildWatchpartySection("Agent output", watchparty.AgentOutput, "fresh"));
        }

        if (!string.IsNullOrWhiteSpace(watchparty.AgentTools))
        {
            details.Add(BuildWatchpartySection("Agent tools", watchparty.AgentTools, "running"));
        }

        return Border(
                VStack(6,
                    TextBlock("AI working...").Bold(),
                    TextBlock(card.ChatProcessing
                            ? "The current turn is still in flight."
                            : card.ChatReceiving
                                ? "Waiting for more streamed chat output."
                                : "Live watchparty signals are active.")
                        .Opacity(0.72)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    VStack(6, details.ToArray())))
            .Padding(10)
            .Background(CardToneBrushes.CreateToneBrush("running", 0x12))
            .CornerRadius(12);
    }

    private static Element BuildWatchpartySection(string title, string text, string tone)
    {
        return Border(
                VStack(4,
                    TextBlock(title).FontSize(12).Opacity(0.72).Bold(),
                    TextBlock(text).Set(block => block.TextWrapping = TextWrapping.WrapWholeWords)))
            .Padding(10)
            .Background(CardToneBrushes.CreateToneBrush(tone, 0x10))
            .CornerRadius(12);
    }

    private static Element IconLabel(string svgPath, string label)
    {
        return HStack(6,
            TextBlock(label == "Pop out" ? "Open" : "Attach").Opacity(0.72),
            TextBlock(label));
    }

    private static string CreateTurnId()
    {
        return $"winui-{Guid.NewGuid():N}";
    }
}