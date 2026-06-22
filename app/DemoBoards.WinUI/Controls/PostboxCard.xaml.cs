using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DemoBoards_WinUI.Controls;

public sealed class PostboxCard : UserControl
{
    private int historyRequestVersion;
    private string currentCardId = string.Empty;
    private readonly List<NativeAttachmentFile> selectedFiles = new();
    private BoardStore? boardStore;
    private bool uploading;
    private string draftTurnId = CreateTurnId();
    private readonly Border ShellBorder;
    private readonly TextBlock TitleText;
    private readonly TextBlock SubtitleText;
    private readonly TextBlock HistoryStatusText;
    private readonly StackPanel HistoryHost;
    private readonly StackPanel FilesHost;
    private readonly Border UploadDropZoneBorder;
    private readonly TextBlock UploadDropHintText;
    private readonly Button BrowseFilesButton;
    private readonly StackPanel SelectedFilesHost;
    private readonly TextBox CommentTextBox;
    private readonly TextBlock UploadStatusText;
    private readonly Button UploadButton;
    private readonly ChatPane ChatPaneView;

    public PostboxCard()
    {
        TitleText = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        SubtitleText = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.68,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        HistoryStatusText = new TextBlock
        {
            Opacity = 0.68,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        HistoryHost = new StackPanel { Spacing = 8 };
        FilesHost = new StackPanel { Spacing = 8 };
        UploadDropHintText = new TextBlock
        {
            Opacity = 0.62,
            Text = "Drop files here or browse to stage them for upload.",
            TextWrapping = TextWrapping.WrapWholeWords
        };
        BrowseFilesButton = new Button
        {
            Content = "Browse Files",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        SelectedFilesHost = new StackPanel
        {
            Spacing = 6,
            Visibility = Visibility.Collapsed
        };
        CommentTextBox = new TextBox
        {
            PlaceholderText = "Add comment (optional)",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 68
        };
        UploadStatusText = new TextBlock
        {
            Opacity = 0.68,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center
        };
        UploadButton = new Button { Content = "Upload" };
        ChatPaneView = new ChatPane();

        var uploadFooter = new Grid { ColumnSpacing = 8 };
        uploadFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        uploadFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        uploadFooter.Children.Add(UploadStatusText);
        Grid.SetColumn(UploadButton, 1);
        uploadFooter.Children.Add(UploadButton);

        UploadDropZoneBorder = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            AllowDrop = true,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Upload submission", FontWeight = FontWeights.SemiBold },
                    UploadDropHintText,
                    BrowseFilesButton,
                    SelectedFilesHost,
                    CommentTextBox,
                    uploadFooter,
                }
            }
        };

        var rootStack = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        TitleText,
                        SubtitleText,
                    }
                },
                new Border
                {
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(12),
                    Background = BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Colors.White),
                    BorderBrush = BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Recent submissions", FontWeight = FontWeights.SemiBold },
                            HistoryStatusText,
                            HistoryHost,
                        }
                    }
                },
                new Border
                {
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(12),
                    Background = BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Colors.White),
                    BorderBrush = BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Stored files", FontWeight = FontWeights.SemiBold },
                            FilesHost,
                        }
                    }
                },
                UploadDropZoneBorder,
                new Border
                {
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(12),
                    Background = BoardTheme.ResolveBrush("BoardSurfaceMutedBrush", Colors.Gainsboro),
                    BorderBrush = BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Live conversation", FontWeight = FontWeights.SemiBold },
                            ChatPaneView,
                        }
                    }
                }
            }
        };

        ShellBorder = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = rootStack
            }
        };
        Content = ShellBorder;

        BrowseFilesButton.Click += OnBrowseFilesClick;
        UploadButton.Click += OnUploadClick;
        UploadDropZoneBorder.DragOver += OnUploadDragOver;
        UploadDropZoneBorder.Drop += OnUploadDrop;
        ChatPaneView.Configure(compact: true, enablePopout: true, title: "Chat");
        ChatPaneView.PopoutRequested += OnChatPopoutRequested;
        Unloaded += OnUnloaded;
    }

    public void Render(BoardCard card)
    {
        if (boardStore is null)
        {
            boardStore = ((App)Application.Current).BoardStore;
            boardStore.StateChanged += OnStateChanged;
        }

        currentCardId = card.Id;
        TitleText.Text = card.Title;
        SubtitleText.Text = $"{card.Id}  •  Submission history and attachments";
        ShellBorder.BorderBrush = CardShell.CreateToneBrush(card.Status, 0x44);
        ShellBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        ChatPaneView.Bind(((App)Application.Current).BoardStore, ((App)Application.Current).BoardClient, card.Id);
        RenderFiles(ParseFiles(card.RawDefinitionJson));
        RenderSelectedFiles();
        UploadStatusText.Text = selectedFiles.Count == 0 ? "Choose one or more files to stage this submission." : $"{selectedFiles.Count} file(s) selected.";
        HistoryStatusText.Text = "Loading submission history...";
        UpdateUploadState();
        _ = LoadHistoryAsync(card.Id);
    }

    private void OnChatPopoutRequested(object? sender, ChatPopoutRequestedEventArgs e)
    {
        MainPage.TryGetCurrent()?.ShowChatPopout(e.CardId, e.Title);
    }

    private async void OnBrowseFilesClick(object sender, RoutedEventArgs e)
    {
        if (uploading)
        {
            return;
        }

        IReadOnlyList<NativeAttachmentFile> files = await NativeFilePicker.PickMultipleAttachmentsAsync();
        StageFiles(files);
    }

    private void OnUploadDragOver(object sender, DragEventArgs e)
    {
        if (uploading)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void OnUploadDrop(object sender, DragEventArgs e)
    {
        if (uploading || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            IReadOnlyList<NativeAttachmentFile> files = await NativeFilePicker.ReadAttachmentsAsync(items);
            StageFiles(files);
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Drop failed: {ex.Message}";
        }
    }

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        if (uploading || string.IsNullOrWhiteSpace(currentCardId) || selectedFiles.Count == 0)
        {
            return;
        }

        uploading = true;
        UpdateUploadState();
        UploadStatusText.Text = "Uploading submission...";

        try
        {
            string text = string.IsNullOrWhiteSpace(CommentTextBox.Text) ? "na" : CommentTextBox.Text.Trim();
            await ((App)Application.Current).BoardClient.AddChatEntryAndAnyAttachmentsAsync(currentCardId, text, draftTurnId, selectedFiles.ToArray());
            selectedFiles.Clear();
            CommentTextBox.Text = string.Empty;
            draftTurnId = CreateTurnId();
            RenderSelectedFiles();
            UploadStatusText.Text = "Submission uploaded.";
            await LoadHistoryAsync(currentCardId);
            RefreshFilesFromStore();
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = ex.Message;
        }
        finally
        {
            uploading = false;
            UpdateUploadState();
        }
    }

    private async System.Threading.Tasks.Task LoadHistoryAsync(string cardId)
    {
        int requestVersion = ++historyRequestVersion;
        try
        {
            string payload = await ((App)Application.Current).BoardClient.CallBoardMcpAsync(
                "inspect.chat-messages-on-cards",
                new { card_id = cardId, all_turns = true });

            if (requestVersion != historyRequestVersion || !string.Equals(cardId, currentCardId, StringComparison.Ordinal))
            {
                return;
            }

            IReadOnlyList<SubmissionGroup> groups = ParseSubmissionGroups(payload);
            RenderHistory(groups);
            HistoryStatusText.Text = groups.Count == 0
                ? "No user submissions recorded yet."
                : $"{groups.Count} submission turn(s) loaded.";
        }
        catch (Exception ex)
        {
            if (requestVersion != historyRequestVersion)
            {
                return;
            }

            HistoryHost.Children.Clear();
            HistoryHost.Children.Add(BuildEmptyState("Submission history is unavailable."));
            HistoryStatusText.Text = ex.Message;
        }
    }

    private void RenderHistory(IReadOnlyList<SubmissionGroup> groups)
    {
        HistoryHost.Children.Clear();
        if (groups.Count == 0)
        {
            HistoryHost.Children.Add(BuildEmptyState("No submissions yet."));
            return;
        }

        foreach (SubmissionGroup group in groups)
        {
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(group.CreatedAt)
                    ? ValueOrFallback(group.TurnId, "Untitled turn")
                    : $"{ValueOrFallback(group.TurnId, "turn")}  •  {group.CreatedAt}",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords
            });

            foreach (string comment in group.Comments)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = comment,
                    TextWrapping = TextWrapping.WrapWholeWords
                });
            }

            if (group.Files.Count > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Files",
                    Opacity = 0.72,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                });

                foreach (SubmissionFile file in group.Files)
                {
                    stack.Children.Add(BuildFileLink(file));
                }
            }

            HistoryHost.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(10),
                Background = BoardTheme.ResolveBrush("BoardSurfaceStrongBrush", Colors.White),
                BorderBrush = BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray),
                BorderThickness = new Thickness(1),
                Child = stack
            });
        }
    }

    private void RenderFiles(IReadOnlyList<StoredFileRecord> files)
    {
        FilesHost.Children.Clear();
        if (files.Count == 0)
        {
            FilesHost.Children.Add(BuildEmptyState("No stored files on this card."));
            return;
        }

        foreach ((StoredFileRecord file, int index) in files.Select((file, index) => (file, index)))
        {
            FilesHost.Children.Add(BuildFileLink(new SubmissionFile(
                index,
                ValueOrFallback(file.Name, ValueOrFallback(file.StoredName, $"file-{index}")),
                file.StoredName,
                file.MimeType,
                file.Size,
                file.UploadedAt)));
        }
    }

    private FrameworkElement BuildFileLink(SubmissionFile file)
    {
        var stack = new StackPanel { Spacing = 2 };
        string href = ((App)Application.Current).BoardClient.GetCardFileUrl(currentCardId, file.Index, file.StoredName);
        var link = new HyperlinkButton
        {
            Content = file.Label,
            HorizontalAlignment = HorizontalAlignment.Left,
            NavigateUri = Uri.TryCreate(href, UriKind.Absolute, out Uri? uri) ? uri : null
        };
        stack.Children.Add(link);
        stack.Children.Add(new TextBlock
        {
            Text = $"{ValueOrFallback(file.MimeType, "unknown type")}  •  {FormatSize(file.Size)}",
            FontSize = 12,
            Opacity = 0.68,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        if (!string.IsNullOrWhiteSpace(file.UploadedAt))
        {
            stack.Children.Add(new TextBlock
            {
                Text = file.UploadedAt,
                FontSize = 12,
                Opacity = 0.58,
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }

        return stack;
    }

    private void RenderSelectedFiles()
    {
        SelectedFilesHost.Children.Clear();
        if (selectedFiles.Count == 0)
        {
            SelectedFilesHost.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedFilesHost.Visibility = Visibility.Visible;
        for (int index = 0; index < selectedFiles.Count; index++)
        {
            NativeAttachmentFile file = selectedFiles[index];
            int removeIndex = index;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = $"{file.Name} • {FormatSize(file.Size)}",
                TextWrapping = TextWrapping.WrapWholeWords,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeButton = new Button
            {
                Content = "Remove",
                HorizontalAlignment = HorizontalAlignment.Right,
                IsEnabled = !uploading
            };
            removeButton.Click += (_, _) =>
            {
                if (removeIndex < 0 || removeIndex >= selectedFiles.Count)
                {
                    return;
                }

                selectedFiles.RemoveAt(removeIndex);
                UploadStatusText.Text = selectedFiles.Count == 0 ? "Choose one or more files to stage this submission." : $"{selectedFiles.Count} file(s) selected.";
                RenderSelectedFiles();
                UpdateUploadState();
            };
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            SelectedFilesHost.Children.Add(new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(10),
                Background = BoardTheme.ResolveBrush("BoardSurfaceMutedBrush", Colors.Gainsboro),
                BorderBrush = BoardTheme.ResolveBrush("BoardBorderBrush", Colors.LightGray),
                BorderThickness = new Thickness(1),
                Child = row
            });
        }
    }

    private void UpdateUploadState()
    {
        BrowseFilesButton.IsEnabled = !uploading;
        CommentTextBox.IsReadOnly = uploading;
        UploadButton.IsEnabled = !uploading && selectedFiles.Count > 0;
        UploadButton.Content = uploading ? "Uploading…" : "Upload";
    }

    private void OnStateChanged(object? sender, BoardStoreChangedEventArgs change)
    {
        if (string.IsNullOrWhiteSpace(currentCardId) || !change.ChangedCardIds.Contains(currentCardId))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshFilesFromStore();
            if (change.ChatsChanged || change.DefinitionsChanged)
            {
                _ = LoadHistoryAsync(currentCardId);
            }
        });
    }

    private void RefreshFilesFromStore()
    {
        BoardCard? card = boardStore?.GetCardDefinitionAndData(currentCardId);
        if (card is null)
        {
            return;
        }

        RenderFiles(ParseFiles(card.RawDefinitionJson));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (boardStore is not null)
        {
            boardStore.StateChanged -= OnStateChanged;
            boardStore = null;
        }
    }

    private void StageFiles(IReadOnlyList<NativeAttachmentFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        foreach (NativeAttachmentFile file in files)
        {
            bool exists = selectedFiles.Any(existing => string.Equals(existing.Name, file.Name, StringComparison.OrdinalIgnoreCase) && existing.Size == file.Size);
            if (!exists)
            {
                selectedFiles.Add(file);
            }
        }

        UploadStatusText.Text = selectedFiles.Count == 0
            ? "Choose one or more files to stage this submission."
            : $"{selectedFiles.Count} file(s) selected.";
        RenderSelectedFiles();
        UpdateUploadState();
    }

    private static IReadOnlyList<SubmissionGroup> ParseSubmissionGroups(string payload)
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
            return Array.Empty<SubmissionGroup>();
        }

        var groups = new List<SubmissionGroup>();
        var byTurn = new Dictionary<string, SubmissionGroupBuilder>(StringComparer.Ordinal);
        int untitledIndex = 0;
        foreach (JsonElement message in messages.EnumerateArray())
        {
            string role = message.TryGetProperty("role", out JsonElement roleElement) ? roleElement.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string turnId = message.TryGetProperty("turn", out JsonElement turnElement) ? turnElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(turnId))
            {
                turnId = $"turn-{++untitledIndex}";
            }

            if (!byTurn.TryGetValue(turnId, out SubmissionGroupBuilder? builder))
            {
                builder = new SubmissionGroupBuilder(turnId);
                byTurn[turnId] = builder;
                groups.Add(builder.Group);
            }

            if (message.TryGetProperty("text", out JsonElement textElement))
            {
                string comment = (textElement.GetString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(comment) && !string.Equals(comment, "na", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Comments.Add(comment);
                }
            }

            if (string.IsNullOrWhiteSpace(builder.Group.CreatedAt)
                && message.TryGetProperty("updated_at", out JsonElement updatedAtElement))
            {
                builder.Group = builder.Group with { CreatedAt = updatedAtElement.GetString() ?? string.Empty };
            }

            if (message.TryGetProperty("files", out JsonElement files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement file in files.EnumerateArray())
                {
                    string storedName = file.TryGetProperty("stored_name", out JsonElement storedNameElement) ? storedNameElement.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(storedName) || !builder.FileKeys.Add(storedName))
                    {
                        continue;
                    }

                    builder.Files.Add(new SubmissionFile(
                        file.TryGetProperty("file_idx", out JsonElement fileIndexElement) && fileIndexElement.TryGetInt32(out int fileIndex) ? fileIndex : builder.Files.Count,
                        file.TryGetProperty("name", out JsonElement nameElement) ? ValueOrFallback(nameElement.GetString(), storedName) : storedName,
                        storedName,
                        file.TryGetProperty("mime_type", out JsonElement mimeTypeElement) ? mimeTypeElement.GetString() : null,
                        file.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long size) ? size : null,
                        file.TryGetProperty("uploaded_at", out JsonElement uploadedAtElement) ? uploadedAtElement.GetString() : null));
                }
            }
        }

        return groups
            .Select(group => group with
            {
                Comments = group.Comments.ToArray(),
                Files = group.Files.ToArray()
            })
            .Where(group => group.Comments.Count > 0 || group.Files.Count > 0)
            .ToArray();
    }

    private static IReadOnlyList<StoredFileRecord> ParseFiles(string rawDefinitionJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawDefinitionJson);
            if (!document.RootElement.TryGetProperty("card_data", out JsonElement cardData)
                || cardData.ValueKind != JsonValueKind.Object
                || !cardData.TryGetProperty("files", out JsonElement files)
                || files.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<StoredFileRecord>();
            }

            return files.EnumerateArray()
                .Select(file => new StoredFileRecord(
                    file.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null,
                    file.TryGetProperty("stored_name", out JsonElement storedNameElement) ? storedNameElement.GetString() : null,
                    file.TryGetProperty("mime_type", out JsonElement mimeTypeElement) ? mimeTypeElement.GetString() : null,
                    file.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long size) ? size : null,
                    file.TryGetProperty("uploaded_at", out JsonElement uploadedAtElement) ? uploadedAtElement.GetString() : null))
                .ToArray();
        }
        catch
        {
            return Array.Empty<StoredFileRecord>();
        }
    }

    private static string FormatSize(long? size)
    {
        if (!size.HasValue || size.Value <= 0)
        {
            return "unknown size";
        }

        if (size.Value < 1024)
        {
            return $"{size.Value} B";
        }

        double kb = size.Value / 1024d;
        if (kb < 1024)
        {
            return $"{Math.Round(kb)} KB";
        }

        return $"{(kb / 1024d):0.0} MB";
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

    private static string ValueOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string CreateTurnId()
    {
        return $"winui-{Guid.NewGuid():N}";
    }

    private sealed record SubmissionGroup(string TurnId, string CreatedAt, IReadOnlyList<string> Comments, IReadOnlyList<SubmissionFile> Files);
    private sealed record SubmissionFile(int Index, string Label, string? StoredName, string? MimeType, long? Size, string? UploadedAt);
    private sealed record StoredFileRecord(string? Name, string? StoredName, string? MimeType, long? Size, string? UploadedAt);

    private sealed class SubmissionGroupBuilder
    {
        public SubmissionGroupBuilder(string turnId)
        {
            Group = new SubmissionGroup(turnId, string.Empty, Array.Empty<string>(), Array.Empty<SubmissionFile>());
        }

        public SubmissionGroup Group { get; set; }

        public List<string> Comments { get; } = new();

        public List<SubmissionFile> Files { get; } = new();

        public HashSet<string> FileKeys { get; } = new(StringComparer.Ordinal);
    }
}
