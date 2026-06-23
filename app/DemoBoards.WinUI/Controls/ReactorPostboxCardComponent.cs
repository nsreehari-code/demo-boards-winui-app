using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorPostboxCardProps(BoardCard Card);

public sealed class ReactorPostboxCardComponent : Component<ReactorPostboxCardProps>
{
    private const int HistoryTurnsPerPage = 8;

    public override Element Render()
    {
        BoardCard card = Props.Card;

        var (_, setRevision) = UseState(string.Empty);
        var (commentText, setCommentText) = UseState(string.Empty);
        var (selectedFiles, setSelectedFiles) = UseState<IReadOnlyList<NativeAttachmentFile>>(Array.Empty<NativeAttachmentFile>());
        var (submitting, setSubmitting) = UseState(false);
        var (historyLoading, setHistoryLoading) = UseState(false);
        var (statusText, setStatusText) = UseState(string.Empty);
        var (viewMode, setViewMode) = UseState("submissions");
        var (messages, setMessages) = UseState<IReadOnlyList<PostboxMessage>>(Array.Empty<PostboxMessage>());
        var (historyCursorTurnId, setHistoryCursorTurnId) = UseState(string.Empty);
        var (hasMoreHistory, setHasMoreHistory) = UseState(false);
        var (draftTurnId, setDraftTurnId) = UseState(CreateTurnId());

        UseEffect(() =>
        {
            EventHandler<DemoBoards_WinUI.State.BoardStoreChangedEventArgs> onStateChanged = (_, change) =>
            {
                if (change.ChangedCardIds.Contains(card.Id))
                {
                    setRevision(Guid.NewGuid().ToString("N"));
                }
            };

            App.Current.BoardStore.StateChanged += onStateChanged;
            return () => App.Current.BoardStore.StateChanged -= onStateChanged;
        }, card.Id);

        UseEffect(() =>
        {
            setCommentText(string.Empty);
            setSelectedFiles(Array.Empty<NativeAttachmentFile>());
            setStatusText(string.Empty);
            setViewMode("submissions");
            setMessages(Array.Empty<PostboxMessage>());
            setHistoryCursorTurnId(string.Empty);
            setHasMoreHistory(false);
            setDraftTurnId(CreateTurnId());
            _ = RefreshLatestMessagesAsync(card.Id, setHistoryLoading, setMessages, setHistoryCursorTurnId, setHasMoreHistory, setStatusText);
        }, card.Id);

        UseEffect(() =>
        {
            _ = RefreshLatestMessagesAsync(card.Id, setHistoryLoading, setMessages, setHistoryCursorTurnId, setHasMoreHistory, _ => { });
        }, card.ChatMessages.Count, card.Id);

        IReadOnlyList<SubmissionGroup> submissions = GroupMessagesByTurn(messages);
        IReadOnlyList<FileRecord> storedFiles = ParseFiles(card.RawDefinitionJson);
        bool canSubmit = selectedFiles.Count > 0 && !submitting;

        return Border(
                VStack(12,
                    HStack(8,
                        ToggleButton("Submissions", viewMode == "submissions", () => setViewMode("submissions")),
                        ToggleButton("Files", viewMode == "files", () => setViewMode("files"))),
                    viewMode == "submissions"
                        ? (Element)BuildSubmissionsView(card, submissions, messages, historyLoading, hasMoreHistory, historyCursorTurnId, setHistoryLoading, setMessages, setHistoryCursorTurnId, setHasMoreHistory, setStatusText)
                        : BuildFilesView(card, storedFiles),
                    selectedFiles.Count > 0
                        ? (Element)HStack(6, selectedFiles.Select((file, index) =>
                            (Element)Border(
                                    HStack(6,
                                        TextBlock(file.Name).FontSize(12),
                                        Button("Remove", () => setSelectedFiles(selectedFiles.Where((_, currentIndex) => currentIndex != index).ToArray()))
                                            .AutomationName($"Remove {file.Name}")
                                            .SubtleButton()))
                                .Padding(8, 4, 8, 4)
                                .Background(CardToneBrushes.CreateToneBrush("running", 0x12))
                                .CornerRadius(10)).ToArray())
                        : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed),
                    TextBox(commentText, setCommentText)
                        .AutomationName("Postbox comment")
                        .PlaceholderText("Add comment (optional)")
                        .Set(textBox => textBox.TextWrapping = TextWrapping.Wrap),
                    HStack(8,
                        TextBlock(string.IsNullOrWhiteSpace(statusText) ? "Attach files and optionally add a comment before uploading." : statusText)
                            .Opacity(0.72)
                            .Flex(grow: 1)
                            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                        Button("Add files", () =>
                        {
                            if (!submitting)
                            {
                                _ = PickFilesAsync(selectedFiles, setSelectedFiles, setStatusText);
                            }
                        }).AutomationName("Add postbox files").SubtleButton(),
                        Button(submitting ? "Uploading..." : "Upload", () =>
                        {
                            if (canSubmit)
                            {
                                _ = UploadAsync(card.Id, commentText, draftTurnId, selectedFiles, setSubmitting, setCommentText, setSelectedFiles, setDraftTurnId, setStatusText, setHistoryLoading, setMessages, setHistoryCursorTurnId, setHasMoreHistory);
                            }
                        }).AutomationName("Upload postbox submission").AccentButton().Set(button => button.IsEnabled = canSubmit)))
            )
            .Padding(14)
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorDefaultBrush"))
            .WithBorder(CardToneBrushes.CreateToneBrush(card.Status, 0x44), 1)
            .CornerRadius(14);
    }

    private static Element ToggleButton(string label, bool active, Action action)
    {
        return Button(label, action)
            .AutomationName(label)
            .Set(button =>
            {
                button.Style = active
                    ? (Style)Application.Current.Resources["BoardFloatingCircleButtonActiveStyle"]
                    : (Style)Application.Current.Resources["BoardFloatingCircleButtonStyle"];
            });
    }

    private static Element BuildSubmissionsView(
        BoardCard card,
        IReadOnlyList<SubmissionGroup> submissions,
        IReadOnlyList<PostboxMessage> currentMessages,
        bool historyLoading,
        bool hasMoreHistory,
        string historyCursorTurnId,
        Action<bool> setHistoryLoading,
        Action<IReadOnlyList<PostboxMessage>> setMessages,
        Action<string> setHistoryCursorTurnId,
        Action<bool> setHasMoreHistory,
        Action<string> setStatusText)
    {
        var items = new List<Element>();
        if (hasMoreHistory && !string.IsNullOrWhiteSpace(historyCursorTurnId))
        {
            items.Add(
                Button(historyLoading ? "Loading previous submissions..." : "Show previous submissions", () =>
                {
                    if (!historyLoading)
                    {
                        _ = LoadMoreHistoryAsync(card.Id, historyCursorTurnId, currentMessages, setHistoryLoading, setMessages, setHistoryCursorTurnId, setHasMoreHistory, setStatusText);
                    }
                }).AutomationName("Load previous postbox submissions").SubtleButton());
        }

        if (submissions.Count == 0)
        {
            items.Add(TextBlock("Use this surface to stage files and upload the first evidence bundle.").Opacity(0.62));
        }
        else
        {
            items.AddRange(submissions.Select(submission =>
            {
                Element comments = submission.Comments.Count == 0
                    ? TextBlock("No comment.").Opacity(0.58)
                    : VStack(4, submission.Comments.Select(comment => (Element)TextBlock(comment).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)).ToArray());

                Element files = submission.Files.Count == 0
                    ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                    : VStack(4, submission.Files.Select(file => (Element)BuildFileLink(card, file)).ToArray());

                return (Element)Border(
                        VStack(6,
                            HStack(8,
                                TextBlock(string.IsNullOrWhiteSpace(submission.CreatedAtLabel) ? submission.TurnId : submission.CreatedAtLabel).Bold().Flex(grow: 1),
                                TextBlock(submission.Files.Count == 0 ? "comment" : $"{submission.Files.Count} file(s)").Opacity(0.62)),
                            comments,
                            files))
                    .Padding(10)
                    .Background(BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Colors.White))
                    .WithBorder(BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray), 1)
                    .CornerRadius(12);
            }));
        }

        return ScrollViewer(VStack(10, items.ToArray()))
            .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto)
            .MinHeight(280);
    }

    private static Element BuildFilesView(BoardCard card, IReadOnlyList<FileRecord> storedFiles)
    {
        if (storedFiles.Count == 0)
        {
            return TextBlock("No uploaded files are attached to this card yet.").Opacity(0.62);
        }

        return ScrollViewer(
                VStack(8, storedFiles.Select(file => (Element)Border(
                        VStack(4,
                            BuildFileLink(card, new SubmissionFileRef(file.Index, file.Name, file.StoredName, file.UploadedAt, file.Size, file.MimeType)),
                            TextBlock(file.Size.HasValue ? $"{file.Size.Value} bytes" : "Unknown size").Opacity(0.62),
                            string.IsNullOrWhiteSpace(file.UploadedAt)
                                ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                                : TextBlock(file.UploadedAt).Opacity(0.62))
                    .Padding(10)
                    .Background(BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Colors.White))
                    .WithBorder(BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray), 1)
                    .CornerRadius(12))).ToArray()))
            .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto)
            .MinHeight(280);
    }

    private static Element BuildFileLink(BoardCard card, SubmissionFileRef file)
    {
        string label = string.IsNullOrWhiteSpace(file.Name) ? ValueOrFallback(file.StoredName, "unnamed") : file.Name!;
        return HyperlinkButton(label, Uri.TryCreate(App.Current.BoardClient.GetCardFileUrl(card.Id, file.Index, file.StoredName), UriKind.Absolute, out Uri? uri) ? uri : null);
    }

    private static async Task PickFilesAsync(IReadOnlyList<NativeAttachmentFile> selectedFiles, Action<IReadOnlyList<NativeAttachmentFile>> setSelectedFiles, Action<string> setStatusText)
    {
        try
        {
            IReadOnlyList<NativeAttachmentFile> files = await NativeFilePicker.PickMultipleAttachmentsAsync(true);
            if (files.Count == 0)
            {
                return;
            }

            List<NativeAttachmentFile> merged = selectedFiles.ToList();
            foreach (NativeAttachmentFile file in files)
            {
                bool exists = merged.Any(existing => existing.Name == file.Name && existing.Size == file.Size);
                if (!exists)
                {
                    merged.Add(file);
                }
            }

            setSelectedFiles(merged);
            setStatusText($"{merged.Count} file(s) staged.");
        }
        catch (Exception ex)
        {
            setStatusText($"File selection failed: {ex.Message}");
        }
    }

    private static async Task UploadAsync(
        string cardId,
        string commentText,
        string draftTurnId,
        IReadOnlyList<NativeAttachmentFile> selectedFiles,
        Action<bool> setSubmitting,
        Action<string> setCommentText,
        Action<IReadOnlyList<NativeAttachmentFile>> setSelectedFiles,
        Action<string> setDraftTurnId,
        Action<string> setStatusText,
        Action<bool> setHistoryLoading,
        Action<IReadOnlyList<PostboxMessage>> setMessages,
        Action<string> setHistoryCursorTurnId,
        Action<bool> setHasMoreHistory)
    {
        setSubmitting(true);
        setStatusText("Uploading submission...");
        try
        {
            string text = string.IsNullOrWhiteSpace(commentText) ? "na" : commentText.Trim();
            await App.Current.BoardClient.AddChatEntryAndAnyAttachmentsAsync(cardId, text, draftTurnId, selectedFiles);
            setCommentText(string.Empty);
            setSelectedFiles(Array.Empty<NativeAttachmentFile>());
            setDraftTurnId(CreateTurnId());
            await RefreshLatestMessagesAsync(cardId, setHistoryLoading, setMessages, setHistoryCursorTurnId, setHasMoreHistory, setStatusText);
            setStatusText("Submission uploaded.");
        }
        catch (Exception ex)
        {
            setStatusText($"Upload failed: {ex.Message}");
        }
        finally
        {
            setSubmitting(false);
        }
    }

    private static async Task RefreshLatestMessagesAsync(string cardId, Action<bool> setHistoryLoading, Action<IReadOnlyList<PostboxMessage>> setMessages, Action<string> setHistoryCursorTurnId, Action<bool> setHasMoreHistory, Action<string> setStatusText)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        setHistoryLoading(true);
        try
        {
            IReadOnlyList<PostboxMessage> latest = await FetchChatHistoryAsync(cardId, string.Empty, HistoryTurnsPerPage);
            setMessages(latest);
            string nextCursor = latest.Select(message => message.Turn).FirstOrDefault(turn => !string.IsNullOrWhiteSpace(turn)) ?? string.Empty;
            setHistoryCursorTurnId(nextCursor);
            setHasMoreHistory(CountDistinctTurns(latest) >= HistoryTurnsPerPage && !string.IsNullOrWhiteSpace(nextCursor));
        }
        catch (Exception ex)
        {
            setStatusText(ex.Message);
        }
        finally
        {
            setHistoryLoading(false);
        }
    }

    private static async Task LoadMoreHistoryAsync(string cardId, string beforeTurnId, IReadOnlyList<PostboxMessage> currentMessages, Action<bool> setHistoryLoading, Action<IReadOnlyList<PostboxMessage>> setMessages, Action<string> setHistoryCursorTurnId, Action<bool> setHasMoreHistory, Action<string> setStatusText)
    {
        if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(beforeTurnId))
        {
            return;
        }

        setHistoryLoading(true);
        try
        {
            IReadOnlyList<PostboxMessage> older = await FetchChatHistoryAsync(cardId, beforeTurnId, HistoryTurnsPerPage);
            if (older.Count == 0)
            {
                setHasMoreHistory(false);
                return;
            }

            setMessages(MergeMessages(older, currentMessages));
            string nextCursor = older.Select(message => message.Turn).FirstOrDefault(turn => !string.IsNullOrWhiteSpace(turn)) ?? string.Empty;
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
        catch (Exception ex)
        {
            setStatusText(ex.Message);
        }
        finally
        {
            setHistoryLoading(false);
        }
    }

    private static IReadOnlyList<PostboxMessage> MergeMessages(IReadOnlyList<PostboxMessage> older, IReadOnlyList<PostboxMessage> current)
    {
        var merged = new List<PostboxMessage>(older);
        foreach (PostboxMessage message in current)
        {
            bool exists = merged.Any(existing => existing.Turn == message.Turn && existing.Role == message.Role && existing.Text == message.Text);
            if (!exists)
            {
                merged.Add(message);
            }
        }

        return merged;
    }

    private static async Task<IReadOnlyList<PostboxMessage>> FetchChatHistoryAsync(string cardId, string beforeTurnId, int turns)
    {
        string payload = await App.Current.BoardClient.CallBoardMcpAsync(
            "inspect.chat-messages-on-cards",
            string.IsNullOrWhiteSpace(beforeTurnId)
                ? new { card_id = cardId, tail_turns = turns }
                : new { card_id = cardId, tail_turns = turns, tail_turns_before_id = beforeTurnId });

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
            return Array.Empty<PostboxMessage>();
        }

        return messages.EnumerateArray()
            .Where(message => message.ValueKind == JsonValueKind.Object)
            .Select(ParseMessage)
            .Where(message => message is not null)
            .Cast<PostboxMessage>()
            .ToArray();
    }

    private static PostboxMessage? ParseMessage(JsonElement message)
    {
        string role = message.TryGetProperty("role", out JsonElement roleElement) && roleElement.ValueKind == JsonValueKind.String ? roleElement.GetString() ?? string.Empty : string.Empty;
        string text = message.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String ? textElement.GetString() ?? string.Empty : string.Empty;
        string turn = message.TryGetProperty("turn", out JsonElement turnElement) && turnElement.ValueKind == JsonValueKind.String ? turnElement.GetString() ?? string.Empty : string.Empty;
        string updatedAt = message.TryGetProperty("updated_at", out JsonElement updatedAtElement) && updatedAtElement.ValueKind == JsonValueKind.String ? updatedAtElement.GetString() ?? string.Empty : string.Empty;
        IReadOnlyList<SubmissionFileRef> files = message.TryGetProperty("files", out JsonElement filesElement) && filesElement.ValueKind == JsonValueKind.Array
            ? filesElement.EnumerateArray().Select(ParseSubmissionFileRef).Where(file => file is not null).Cast<SubmissionFileRef>().ToArray()
            : Array.Empty<SubmissionFileRef>();

        return new PostboxMessage(role, text, turn, updatedAt, files);
    }

    private static SubmissionFileRef? ParseSubmissionFileRef(JsonElement file)
    {
        if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("stored_name", out JsonElement storedNameElement) || storedNameElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        int index = file.TryGetProperty("file_idx", out JsonElement indexElement) && indexElement.TryGetInt32(out int parsedIndex) ? parsedIndex : 0;
        string? name = file.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
        string storedName = storedNameElement.GetString() ?? string.Empty;
        string? uploadedAt = file.TryGetProperty("uploaded_at", out JsonElement uploadedAtElement) && uploadedAtElement.ValueKind == JsonValueKind.String ? uploadedAtElement.GetString() : null;
        long? size = file.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long parsedSize) ? parsedSize : null;
        string? mimeType = file.TryGetProperty("mime_type", out JsonElement mimeTypeElement) && mimeTypeElement.ValueKind == JsonValueKind.String ? mimeTypeElement.GetString() : null;
        return new SubmissionFileRef(index, name, storedName, uploadedAt, size, mimeType);
    }

    private static IReadOnlyList<SubmissionGroup> GroupMessagesByTurn(IReadOnlyList<PostboxMessage> messages)
    {
        var groups = new List<SubmissionGroup>();
        var byTurn = new Dictionary<string, SubmissionGroupBuilder>(StringComparer.Ordinal);

        foreach (PostboxMessage message in messages)
        {
            if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string turnId = string.IsNullOrWhiteSpace(message.Turn) ? $"__message-{groups.Count}" : message.Turn;
            if (!byTurn.TryGetValue(turnId, out SubmissionGroupBuilder? builder))
            {
                builder = new SubmissionGroupBuilder(turnId);
                byTurn[turnId] = builder;
                groups.Add(builder.BuildPlaceholder());
            }

            if (!string.IsNullOrWhiteSpace(message.UpdatedAt) && string.IsNullOrWhiteSpace(builder.CreatedAtLabel))
            {
                builder.CreatedAtLabel = message.UpdatedAt;
            }

            string normalizedComment = NormalizeCommentText(message.Text);
            if (!string.IsNullOrWhiteSpace(normalizedComment))
            {
                builder.Comments.Add(normalizedComment);
            }

            foreach (SubmissionFileRef file in message.Files)
            {
                if (builder.FileKeys.Add(file.StoredName ?? string.Empty))
                {
                    builder.Files.Add(file);
                    if (!string.IsNullOrWhiteSpace(file.UploadedAt) && string.IsNullOrWhiteSpace(builder.CreatedAtLabel))
                    {
                        builder.CreatedAtLabel = file.UploadedAt;
                    }
                }
            }
        }

        return byTurn.Values
            .Select(builder => new SubmissionGroup(builder.TurnId, builder.CreatedAtLabel, builder.Comments.ToArray(), builder.Files.ToArray()))
            .Where(group => group.Comments.Count > 0 || group.Files.Count > 0)
            .ToArray();
    }

    private static int CountDistinctTurns(IReadOnlyList<PostboxMessage> messages)
    {
        return messages.Select(message => message.Turn).Where(turn => !string.IsNullOrWhiteSpace(turn)).Distinct(StringComparer.Ordinal).Count();
    }

    private static string NormalizeCommentText(string text)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || string.Equals(normalized, "na", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized;
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
                .Select((file, index) => new FileRecord(
                    index,
                    file.TryGetProperty("name", out JsonElement name) ? name.GetString() : null,
                    file.TryGetProperty("stored_name", out JsonElement storedName) ? storedName.GetString() : null,
                    file.TryGetProperty("mime_type", out JsonElement mimeType) ? mimeType.GetString() : null,
                    file.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long parsedSize) ? parsedSize : null,
                    file.TryGetProperty("uploaded_at", out JsonElement uploadedAt) ? uploadedAt.GetString() : null))
                .Where(file => !string.IsNullOrWhiteSpace(file.StoredName))
                .OrderByDescending(file => file.UploadedAt ?? string.Empty)
                .ToArray();
        }
        catch
        {
            return Array.Empty<FileRecord>();
        }
    }

    private static string ValueOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string CreateTurnId()
    {
        return $"winui-postbox-{Guid.NewGuid():N}";
    }

    private sealed record PostboxMessage(string Role, string Text, string Turn, string UpdatedAt, IReadOnlyList<SubmissionFileRef> Files);
    private sealed record SubmissionFileRef(int Index, string? Name, string? StoredName, string? UploadedAt, long? Size, string? MimeType);
    private sealed record SubmissionGroup(string TurnId, string CreatedAtLabel, IReadOnlyList<string> Comments, IReadOnlyList<SubmissionFileRef> Files);
    private sealed record FileRecord(int Index, string? Name, string? StoredName, string? MimeType, long? Size, string? UploadedAt);

    private sealed class SubmissionGroupBuilder
    {
        public SubmissionGroupBuilder(string turnId)
        {
            TurnId = turnId;
        }

        public string TurnId { get; }
        public string CreatedAtLabel { get; set; } = string.Empty;
        public List<string> Comments { get; } = new();
        public List<SubmissionFileRef> Files { get; } = new();
        public HashSet<string> FileKeys { get; } = new(StringComparer.Ordinal);

        public SubmissionGroup BuildPlaceholder()
        {
            return new SubmissionGroup(TurnId, string.Empty, Array.Empty<string>(), Array.Empty<SubmissionFileRef>());
        }
    }
}