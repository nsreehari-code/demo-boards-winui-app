using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.State;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DemoBoards_WinUI.Controls;

public sealed class ChatPane : UserControl
{
    private const string AgentOutputChannel = "agent-output";
    private const string AgentToolsChannel = "agent-tools";
    private const int HistoryTurnsPerPage = 5;

    private BoardStore? boardStore;
    private EmbeddedBoardClient? boardClient;
    private string? currentCardId;
    private bool subscribed;
    private bool sending;
    private bool historyLoading;
    private string draftTurnId = CreateTurnId();
    private string historyAnchorTurnId = string.Empty;
    private string historyCursorTurnId = string.Empty;
    private bool hasMoreHistory;
    private bool isCompact;
    private bool canPopout;
    private string paneTitle = "Chat";
    private readonly List<string> draftAttachmentNames = new();
    private readonly List<BoardChatMessage> historyMessages = new();
    private readonly Grid HeaderRow;
    private readonly TextBlock HeaderTitleText;
    private readonly Button PopoutButton;
    private readonly Button HistoryButton;
    private readonly StackPanel HistoryHost;
    private readonly StackPanel MessagesHost;
    private readonly Border WorkingBubbleBorder;
    private readonly TextBlock WorkingTitleText;
    private readonly TextBlock WorkingStatusText;
    private readonly StackPanel WorkingDetailsHost;
    private readonly Border DropZoneBorder;
    private readonly TextBox ComposerTextBox;
    private readonly TextBlock DropHintText;
    private readonly StackPanel AttachmentHost;
    private readonly TextBlock StatusText;
    private readonly Button AttachButton;
    private readonly Button SendButton;

    public ChatPane()
    {
        HeaderTitleText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        PopoutButton = new Button();
        HeaderRow = new Grid
        {
            ColumnSpacing = 8,
            Visibility = Visibility.Collapsed
        };
        HeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        HeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        HeaderRow.Children.Add(HeaderTitleText);
        Grid.SetColumn(PopoutButton, 1);
        HeaderRow.Children.Add(PopoutButton);

        HistoryButton = new Button
        {
            Content = "Show previous messages",
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        HistoryHost = new StackPanel { Spacing = 10 };
        MessagesHost = new StackPanel { Spacing = 10 };
        WorkingTitleText = new TextBlock { FontWeight = FontWeights.SemiBold };
        WorkingStatusText = new TextBlock
        {
            Opacity = 0.72,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        WorkingDetailsHost = new StackPanel { Spacing = 6 };
        WorkingBubbleBorder = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    WorkingTitleText,
                    WorkingStatusText,
                    WorkingDetailsHost,
                }
            }
        };

        ComposerTextBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            PlaceholderText = "Type a message"
        };
        DropHintText = new TextBlock
        {
            Opacity = 0.6,
            Text = "Drop files here or use Attach file to stage them for the next send.",
            TextWrapping = TextWrapping.WrapWholeWords
        };
        DropZoneBorder = new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(12),
            AllowDrop = true,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    ComposerTextBox,
                    DropHintText,
                }
            }
        };
        AttachmentHost = new StackPanel
        {
            Spacing = 6,
            Visibility = Visibility.Collapsed
        };
        StatusText = new TextBlock
        {
            Opacity = 0.72,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center
        };
        AttachButton = new Button();
        SendButton = new Button { Content = "Send" };

        var messageStack = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                HistoryButton,
                HistoryHost,
                MessagesHost,
                WorkingBubbleBorder,
            }
        };

        var footerGrid = new Grid { ColumnSpacing = 8 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.Children.Add(StatusText);
        Grid.SetColumn(AttachButton, 1);
        footerGrid.Children.Add(AttachButton);
        Grid.SetColumn(SendButton, 2);
        footerGrid.Children.Add(SendButton);

        var composerStack = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                DropZoneBorder,
                AttachmentHost,
                footerGrid,
            }
        };

        var root = new Grid { RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(HeaderRow);
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = messageStack
        };
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);
        Grid.SetRow(composerStack, 2);
        root.Children.Add(composerStack);
        Content = root;

        PopoutButton.Click += OnPopoutClick;
        HistoryButton.Click += OnLoadHistoryClick;
        ComposerTextBox.TextChanged += OnComposerTextChanged;
        DropZoneBorder.DragOver += OnComposerDragOver;
        DropZoneBorder.Drop += OnComposerDrop;
        AttachButton.Click += OnAttachClick;
        SendButton.Click += OnSendClick;
        Unloaded += OnUnloaded;
        ApplyPresentationMode();
    }

    public event EventHandler<ChatPopoutRequestedEventArgs>? PopoutRequested;

    public void Configure(bool compact, bool enablePopout = false, string? title = null)
    {
        isCompact = compact;
        canPopout = enablePopout;
        paneTitle = string.IsNullOrWhiteSpace(title) ? "Chat" : title.Trim();
        ApplyPresentationMode();
    }

    public void Bind(BoardStore nextBoardStore, EmbeddedBoardClient nextBoardClient, string cardId)
    {
        if (boardStore is not null)
        {
            boardStore.StateChanged -= OnStateChanged;
        }

        boardStore = nextBoardStore;
        boardClient = nextBoardClient;
        currentCardId = cardId;
        historyMessages.Clear();
        historyAnchorTurnId = string.Empty;
        historyCursorTurnId = string.Empty;
        hasMoreHistory = false;
        historyLoading = false;
        boardStore.StateChanged += OnStateChanged;
        _ = EnsureSubscriptionsAsync();
        RenderCurrent();
    }

    private void Render(BoardCard card, BoardWatchpartyState watchparty)
    {
        EnsureHistoryAnchor(card);
        RenderHistory();
        MessagesHost.Children.Clear();

        if (card.ChatMessages.Count == 0)
        {
            MessagesHost.Children.Add(new TextBlock
            {
                Text = "No chat messages on this card yet.",
                Opacity = 0.6,
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }
        else
        {
            foreach (BoardChatMessage message in card.ChatMessages)
            {
                MessagesHost.Children.Add(BuildMessage(message));
            }
        }

        RenderWorkingBubble(card, watchparty);

        StatusText.Text = card.ChatProcessing
            ? "Agent is processing the current turn."
            : card.ChatReceiving
                ? "Waiting for more streamed chat output."
                : sending
                    ? "Sending chat message..."
                    : draftAttachmentNames.Count > 0
                        ? $"{draftAttachmentNames.Count} attachment(s) staged for the next send."
                        : "Ready to send a message.";
        RenderDraftAttachments();
        UpdateComposerState();
    }

    private void RenderCurrent()
    {
        if (boardStore is null || string.IsNullOrWhiteSpace(currentCardId))
        {
            HistoryHost.Children.Clear();
            WorkingBubbleBorder.Visibility = Visibility.Collapsed;
            MessagesHost.Children.Clear();
            StatusText.Text = string.Empty;
            UpdateComposerState();
            return;
        }

        BoardCard? card = boardStore.GetCardDefinitionAndData(currentCardId);
        if (card is null)
        {
            HistoryHost.Children.Clear();
            WorkingBubbleBorder.Visibility = Visibility.Collapsed;
            MessagesHost.Children.Clear();
            StatusText.Text = "Card not found.";
            UpdateComposerState();
            return;
        }

        BoardWatchpartyState watchparty = boardStore.GetCardWatchparty(currentCardId);
        Render(card, watchparty);
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        string text = ComposerTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || sending)
        {
            return;
        }

        sending = true;
        UpdateComposerState();
        StatusText.Text = "Sending chat message...";

        try
        {
            await boardClient.SendChatAsync(currentCardId, text, draftTurnId);
            ComposerTextBox.Text = string.Empty;
            draftAttachmentNames.Clear();
            draftTurnId = CreateTurnId();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Chat send failed: {ex.Message}";
        }
        finally
        {
            sending = false;
            RenderCurrent();
        }
    }

    private async void OnLoadHistoryClick(object sender, RoutedEventArgs e)
    {
        if (historyLoading || string.IsNullOrWhiteSpace(historyCursorTurnId))
        {
            return;
        }

        await LoadHistoryBeforeAsync(historyCursorTurnId);
    }

    private void OnPopoutClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        PopoutRequested?.Invoke(this, new ChatPopoutRequestedEventArgs(currentCardId, paneTitle));
    }

    private void OnComposerTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateComposerState();
    }

    private void UpdateComposerState()
    {
        ComposerTextBox.IsReadOnly = sending;
        AttachButton.IsEnabled = !sending && !string.IsNullOrWhiteSpace(currentCardId);
        SendButton.IsEnabled = !sending && !string.IsNullOrWhiteSpace(currentCardId) && !string.IsNullOrWhiteSpace(ComposerTextBox.Text);
    }

    private async void OnAttachClick(object sender, RoutedEventArgs e)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId) || sending)
        {
            return;
        }

        try
        {
            NativeAttachmentFile? file = await NativeFilePicker.PickSingleAttachmentAsync();
            if (file is null)
            {
                return;
            }

            await UploadDraftAttachmentsAsync(new[] { file });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Attachment upload failed: {ex.Message}";
        }
    }

    private void OnComposerDragOver(object sender, DragEventArgs e)
    {
        if (sending || string.IsNullOrWhiteSpace(currentCardId))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void OnComposerDrop(object sender, DragEventArgs e)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId) || sending || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            IReadOnlyList<NativeAttachmentFile> files = await NativeFilePicker.ReadAttachmentsAsync(items);
            if (files.Count == 0)
            {
                return;
            }

            await UploadDraftAttachmentsAsync(files);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Attachment drop failed: {ex.Message}";
        }
    }

    private void OnStateChanged(object? sender, BoardStoreChangedEventArgs change)
    {
        if (string.IsNullOrWhiteSpace(currentCardId) || !change.ChangedCardIds.Contains(currentCardId))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(RenderCurrent);
    }

    private void EnsureHistoryAnchor(BoardCard card)
    {
        if (!string.IsNullOrWhiteSpace(historyAnchorTurnId))
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

        historyAnchorTurnId = firstTurn;
        historyCursorTurnId = firstTurn;
        hasMoreHistory = true;
        _ = LoadHistoryBeforeAsync(historyAnchorTurnId);
    }

    private async System.Threading.Tasks.Task LoadHistoryBeforeAsync(string beforeTurnId)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId) || string.IsNullOrWhiteSpace(beforeTurnId))
        {
            return;
        }

        historyLoading = true;
        RenderHistory();
        try
        {
            string payload = await boardClient.CallBoardMcpAsync(
                "inspect.chat-messages-on-cards",
                new { card_id = currentCardId, tail_turns = HistoryTurnsPerPage, tail_turns_before_id = beforeTurnId });
            IReadOnlyList<BoardChatMessage> older = ParseChatMessages(payload);
            if (older.Count == 0)
            {
                hasMoreHistory = false;
                return;
            }

            MergeHistoryMessages(older);
            string nextCursor = older
                .Select(message => message.Turn?.Trim() ?? string.Empty)
                .FirstOrDefault(turn => !string.IsNullOrWhiteSpace(turn))
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nextCursor) || string.Equals(nextCursor, beforeTurnId, StringComparison.Ordinal))
            {
                hasMoreHistory = false;
            }
            else
            {
                historyCursorTurnId = nextCursor;
                hasMoreHistory = CountDistinctTurns(older) >= HistoryTurnsPerPage;
            }
        }
        catch
        {
            hasMoreHistory = false;
        }
        finally
        {
            historyLoading = false;
            DispatcherQueue.TryEnqueue(RenderHistory);
        }
    }

    private async System.Threading.Tasks.Task EnsureSubscriptionsAsync()
    {
        if (subscribed || boardClient is null || string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        subscribed = true;
        try
        {
            await boardClient.SubscribeCardChatsAsync(currentCardId);
            await boardClient.SubscribeWatchpartyAsync(currentCardId, AgentOutputChannel);
            await boardClient.SubscribeWatchpartyAsync(currentCardId, AgentToolsChannel);
        }
        catch
        {
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!subscribed || boardClient is null || string.IsNullOrWhiteSpace(currentCardId))
        {
            return;
        }

        try
        {
            await boardClient.UnsubscribeWatchpartyAsync(currentCardId, AgentToolsChannel);
            await boardClient.UnsubscribeWatchpartyAsync(currentCardId, AgentOutputChannel);
            await boardClient.UnsubscribeCardChatsAsync(currentCardId);
        }
        catch
        {
        }
        finally
        {
            subscribed = false;
            if (boardStore is not null)
            {
                boardStore.StateChanged -= OnStateChanged;
            }
        }
    }

    private static UIElement BuildMessage(BoardChatMessage message)
    {
        var border = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = message.Role == "user"
                ? CardShell.CreateToneBrush("running", 0x16)
                : CardShell.CreateToneBrush(message.Processing ? "running" : "fresh", 0x12)
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message.Turn) ? message.Role : $"{message.Role} • {message.Turn}",
            FontSize = 12,
            Opacity = 0.72,
            FontWeight = FontWeights.SemiBold
        });

        var markdown = new BoardMarkdown();
        markdown.Render(message.Text);
        stack.Children.Add(markdown);

        border.Child = stack;
        return border;
    }

    private void ApplyPresentationMode()
    {
        HeaderRow.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;
        HeaderTitleText.Text = paneTitle;
        PopoutButton.Visibility = canPopout ? Visibility.Visible : Visibility.Collapsed;
        PopoutButton.Content = BuildIconButtonContent(HostIconSources.ChatPopout, isCompact ? "Pop out" : "Open full chat");
        DropZoneBorder.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        ComposerTextBox.MinHeight = isCompact ? 40 : 84;
        ComposerTextBox.PlaceholderText = isCompact ? "Send a message" : "Type a message";
        AttachButton.Content = BuildIconButtonContent(HostIconSources.ChatAttach, isCompact ? "Attach" : "Attach File");
    }

    private static object BuildIconButtonContent(string svgPath, string label)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new Image
        {
            Width = 14,
            Height = 14,
            Source = HostIconSources.CreateSvg(svgPath),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        return stack;
    }

    private static UIElement BuildWatchpartySection(string title, string text, string tone)
    {
        var border = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = CardShell.CreateToneBrush(tone, 0x10)
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Opacity = 0.72,
            FontWeight = FontWeights.SemiBold
        });

        var markdown = new BoardMarkdown();
        markdown.Render(text);
        stack.Children.Add(markdown);

        border.Child = stack;
        return border;
    }

    private void RenderHistory()
    {
        HistoryHost.Children.Clear();
        HistoryButton.Visibility = hasMoreHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = historyLoading ? "Loading previous messages..." : "Show previous messages";
        HistoryButton.IsEnabled = !historyLoading && hasMoreHistory;
        if (historyMessages.Count == 0)
        {
            return;
        }

        foreach (BoardChatMessage message in historyMessages)
        {
            HistoryHost.Children.Add(BuildMessage(message));
        }
    }

    private void RenderWorkingBubble(BoardCard card, BoardWatchpartyState watchparty)
    {
        bool show = card.ChatProcessing
            || card.ChatReceiving
            || !string.IsNullOrWhiteSpace(watchparty.AgentOutput)
            || !string.IsNullOrWhiteSpace(watchparty.AgentTools);
        if (!show)
        {
            WorkingBubbleBorder.Visibility = Visibility.Collapsed;
            WorkingDetailsHost.Children.Clear();
            return;
        }

        WorkingBubbleBorder.Visibility = Visibility.Visible;
        WorkingBubbleBorder.Background = CardShell.CreateToneBrush("running", 0x12);
        WorkingTitleText.Text = "AI working...";
        WorkingStatusText.Text = card.ChatProcessing
            ? "The current turn is still in flight."
            : card.ChatReceiving
                ? "Waiting for more streamed chat output."
                : "Live watchparty signals are active.";
        WorkingDetailsHost.Children.Clear();
        if (!string.IsNullOrWhiteSpace(watchparty.AgentOutput))
        {
            WorkingDetailsHost.Children.Add(BuildWatchpartySection("Agent output", watchparty.AgentOutput, "fresh"));
        }

        if (!string.IsNullOrWhiteSpace(watchparty.AgentTools))
        {
            WorkingDetailsHost.Children.Add(BuildWatchpartySection("Agent tools", watchparty.AgentTools, "running"));
        }
    }

    private void RenderDraftAttachments()
    {
        AttachmentHost.Children.Clear();
        if (draftAttachmentNames.Count == 0)
        {
            AttachmentHost.Visibility = Visibility.Collapsed;
            return;
        }

        AttachmentHost.Visibility = Visibility.Visible;
        foreach (string fileName in draftAttachmentNames)
        {
            AttachmentHost.Children.Add(new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(10),
                Background = CardShell.CreateToneBrush("running", 0x12),
                Child = new TextBlock
                {
                    Text = fileName,
                    FontSize = 12,
                    TextWrapping = TextWrapping.WrapWholeWords
                }
            });
        }
    }

    private void MergeHistoryMessages(IReadOnlyList<BoardChatMessage> older)
    {
        var seeded = older.ToList();
        seeded.AddRange(historyMessages);
        historyMessages.Clear();
        historyMessages.AddRange(seeded
            .GroupBy(message => $"{message.Turn}|{message.Role}|{message.Text}", StringComparer.Ordinal)
            .Select(group => group.First()));
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

    private static string CreateTurnId()
    {
        return $"winui-{Guid.NewGuid():N}";
    }

    private async System.Threading.Tasks.Task UploadDraftAttachmentsAsync(IReadOnlyList<NativeAttachmentFile> files)
    {
        if (boardClient is null || string.IsNullOrWhiteSpace(currentCardId) || files.Count == 0)
        {
            return;
        }

        sending = true;
        UpdateComposerState();

        try
        {
            for (int index = 0; index < files.Count; index++)
            {
                NativeAttachmentFile file = files[index];
                StatusText.Text = files.Count == 1
                    ? $"Uploading {file.Name}..."
                    : $"Uploading {index + 1} of {files.Count}: {file.Name}...";
                await boardClient.AddChatAttachmentAsync(currentCardId, draftTurnId, file);
                draftAttachmentNames.Add(file.Name);
            }

            StatusText.Text = files.Count == 1
                ? $"Attached {files[0].Name}."
                : $"Attached {files.Count} files.";
        }
        finally
        {
            sending = false;
            RenderDraftAttachments();
            UpdateComposerState();
        }
    }
}

public sealed record ChatPopoutRequestedEventArgs(string CardId, string Title);
