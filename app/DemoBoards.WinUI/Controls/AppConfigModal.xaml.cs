using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using DemoBoards_WinUI.Config;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class AppConfigModal : UserControl
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    private readonly TextBlock BoardIdText;
    private readonly TextBlock StatusText;
    private readonly TextBox AddBoardIdTextBox;
    private readonly TextBox AddBoardLabelTextBox;
    private readonly TextBox AddBoardPageTitleTextBox;
    private readonly TextBox AddBoardPageSubtitleTextBox;
    private readonly ComboBox AddBoardAiComboBox;
    private readonly ComboBox AddBoardAiWorkspaceTemplateComboBox;
    private readonly ComboBox AddBoardUiTemplateComboBox;
    private readonly ComboBox AddBoardRefsTemplateComboBox;
    private readonly ComboBox TemplateComboBox;
    private readonly Button AddBoardButton;
    private readonly TextBlock TemplateHelpText;
    private readonly TextBlock HostConfigPathsText;
    private readonly Button PreviewEffectiveConfigButton;
    private readonly TextBox HostConfigPreviewTextBox;
    private readonly TextBox EffectiveBoardConfigPreviewTextBox;
    private readonly Button ImportBoardButton;
    private readonly Button ExportBoardButton;
    private readonly Button RefreshBoardButton;
    private readonly Button RunSmokeButton;
    private readonly ComboBox TemplateIngestComboBox;
    private readonly Button PreviewTemplateButton;
    private readonly Button ApplyTemplateButton;
    private readonly TextBlock TemplatePreviewText;
    private readonly ComboBox ThemePackComboBox;
    private readonly TextBox UiJsonTextBox;
    private readonly TextBox MetadataJsonTextBox;
    private readonly TextBox LayoutJsonTextBox;
    private readonly Button SaveButton;

    private string currentBoardId = string.Empty;
    private string currentRawBoardJson = "{}";
    private string? pendingTemplatePayloadJson;
    private bool suppressThemeSelectionSync;
    private WinUiHostTemplateCatalog? hostTemplateCatalog;

    public AppConfigModal()
    {
        BoardIdText = CreateSectionTitleBlock(20, FontWeights.SemiBold);
        StatusText = CreateHintBlock();
        AddBoardIdTextBox = new TextBox { PlaceholderText = "board-id" };
        AddBoardLabelTextBox = new TextBox { PlaceholderText = "Label" };
        AddBoardPageTitleTextBox = new TextBox { PlaceholderText = "Page Title" };
        AddBoardPageSubtitleTextBox = new TextBox { PlaceholderText = "Page Subtitle" };
        AddBoardAiComboBox = new ComboBox { PlaceholderText = "AI" };
        AddBoardAiWorkspaceTemplateComboBox = new ComboBox { PlaceholderText = "AI Workspace Template" };
        AddBoardUiTemplateComboBox = new ComboBox { PlaceholderText = "UI Template" };
        AddBoardRefsTemplateComboBox = new ComboBox { PlaceholderText = "Refs Template" };
        TemplateComboBox = new ComboBox { PlaceholderText = "Optional card template", DisplayMemberPath = nameof(SampleTemplateEntry.Label), SelectedValuePath = nameof(SampleTemplateEntry.Key) };
        AddBoardButton = new Button { Content = "Add board" };
        TemplateHelpText = CreateHintBlock();
        HostConfigPathsText = CreateHintBlock();
        PreviewEffectiveConfigButton = new Button { Content = "Preview effective board config" };
        HostConfigPreviewTextBox = CreateEditorTextBox(120, true);
        EffectiveBoardConfigPreviewTextBox = CreateEditorTextBox(180, true);
        ImportBoardButton = new Button { Content = "Import board" };
        ExportBoardButton = new Button { Content = "Export board" };
        RefreshBoardButton = new Button { Content = "Refresh workspace bootstrap" };
        RunSmokeButton = new Button { Content = "Run tests" };
        TemplateIngestComboBox = new ComboBox { PlaceholderText = "Select template", DisplayMemberPath = nameof(SampleTemplateEntry.Label), SelectedValuePath = nameof(SampleTemplateEntry.Key) };
        PreviewTemplateButton = new Button { Content = "Preview ingest" };
        ApplyTemplateButton = new Button { Content = "Ingest template" };
        TemplatePreviewText = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.WrapWholeWords };
        ThemePackComboBox = new ComboBox();
        UiJsonTextBox = CreateEditorTextBox(160, false);
        MetadataJsonTextBox = CreateEditorTextBox(140, false);
        LayoutJsonTextBox = CreateEditorTextBox(180, false);
        SaveButton = new Button { Content = "Save board config" };

        ThemePackComboBox.SelectionChanged += OnThemePackSelectionChanged;
        AddBoardButton.Click += OnAddBoardClick;
        PreviewEffectiveConfigButton.Click += OnPreviewEffectiveConfigClick;
        ImportBoardButton.Click += OnImportBoardClick;
        ExportBoardButton.Click += OnExportBoardClick;
        RefreshBoardButton.Click += OnRefreshBoardClick;
        RunSmokeButton.Click += OnRunSmokeClick;
        PreviewTemplateButton.Click += OnPreviewTemplateClick;
        ApplyTemplateButton.Click += OnApplyTemplateClick;
        SaveButton.Click += OnSaveClick;

        Content = BuildContent();
        ThemePackComboBox.ItemsSource = BoardTheme.ThemePackIds;
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Spacing = 14 };

        root.Children.Add(CreateSectionCard(new Thickness(16, 14, 16, 14), new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateFieldLabel("Board"),
                BoardIdText,
                StatusText,
            }
        }));

        var addBoardGrid = CreateTwoColumnGrid(5);
        AddLabeledField(addBoardGrid, 0, 0, "Board id", AddBoardIdTextBox);
        AddLabeledField(addBoardGrid, 0, 1, "Label", AddBoardLabelTextBox);
        AddLabeledField(addBoardGrid, 1, 0, "Page title", AddBoardPageTitleTextBox);
        AddLabeledField(addBoardGrid, 1, 1, "Page subtitle", AddBoardPageSubtitleTextBox);
        AddLabeledField(addBoardGrid, 2, 0, "AI", AddBoardAiComboBox);
        AddLabeledField(addBoardGrid, 2, 1, "AI workspace template", AddBoardAiWorkspaceTemplateComboBox);
        AddLabeledField(addBoardGrid, 3, 0, "UI template", AddBoardUiTemplateComboBox);
        AddLabeledField(addBoardGrid, 3, 1, "Refs template", AddBoardRefsTemplateComboBox);
        AddLabeledField(addBoardGrid, 4, 0, "Optional card template", TemplateComboBox);
        AddLabeledField(addBoardGrid, 4, 1, "Action", AddBoardButton, true);
        root.Children.Add(CreateSectionCard(new Thickness(14), new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Add Board"),
                CreateHintBlock("Create a new managed board and optionally seed it from a template."),
                addBoardGrid,
                TemplateHelpText,
            }
        }));

        root.Children.Add(CreateSectionCard(new Thickness(14), new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Host Runtime Config"),
                HostConfigPathsText,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { PreviewEffectiveConfigButton } },
                CreateFieldLabel("Resolved host summary"),
                HostConfigPreviewTextBox,
                CreateFieldLabel("Resolved effective board config"),
                EffectiveBoardConfigPreviewTextBox,
            }
        }));

        var importExportGrid = CreateTwoColumnGrid(2);
        importExportGrid.RowSpacing = 8;
        importExportGrid.ColumnSpacing = 8;
        Grid.SetRow(ImportBoardButton, 0);
        Grid.SetColumn(ImportBoardButton, 0);
        Grid.SetRow(ExportBoardButton, 0);
        Grid.SetColumn(ExportBoardButton, 1);
        Grid.SetRow(RefreshBoardButton, 1);
        Grid.SetColumn(RefreshBoardButton, 0);
        Grid.SetRow(RunSmokeButton, 1);
        Grid.SetColumn(RunSmokeButton, 1);
        importExportGrid.Children.Add(ImportBoardButton);
        importExportGrid.Children.Add(ExportBoardButton);
        importExportGrid.Children.Add(RefreshBoardButton);
        importExportGrid.Children.Add(RunSmokeButton);
        root.Children.Add(CreateSectionCard(new Thickness(14), new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Board Import / Export"),
                CreateHintBlock("Move runtime dumps in and out, refresh the workspace bootstrap, or run board smoke checks."),
                importExportGrid,
            }
        }));

        var templateGrid = new Grid { ColumnSpacing = 8 };
        templateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        templateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        templateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(TemplateIngestComboBox, 0);
        Grid.SetColumn(PreviewTemplateButton, 1);
        Grid.SetColumn(ApplyTemplateButton, 2);
        templateGrid.Children.Add(TemplateIngestComboBox);
        templateGrid.Children.Add(PreviewTemplateButton);
        templateGrid.Children.Add(ApplyTemplateButton);
        root.Children.Add(CreateSectionCard(new Thickness(14), new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Template Card Ingest"),
                CreateHintBlock("Preview the card delta first, then ingest into the current board when the result looks right."),
                templateGrid,
                TemplatePreviewText,
            }
        }));

        root.Children.Add(CreateSectionCard(new Thickness(14), new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Board Presentation"),
                CreateHintBlock("Edit the UI payload, metadata, theme pack, and saved canvas layout together so visual changes stay coherent."),
                CreateFieldLabel("Theme pack"),
                ThemePackComboBox,
                CreateFieldLabel("UI config"),
                UiJsonTextBox,
                CreateFieldLabel("Metadata"),
                MetadataJsonTextBox,
                CreateFieldLabel("Canvas layout"),
                LayoutJsonTextBox,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { SaveButton } },
            }
        }));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };
    }

    private static Grid CreateTwoColumnGrid(int rowCount)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int index = 0; index < rowCount; index += 1)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    private static void AddLabeledField(Grid grid, int row, int column, string label, Control control, bool alignBottom = false)
    {
        var host = new StackPanel { Spacing = 6 };
        host.Children.Add(CreateFieldLabel(label));
        host.Children.Add(control);
        if (alignBottom)
        {
            host.VerticalAlignment = VerticalAlignment.Bottom;
        }

        Grid.SetRow(host, row);
        Grid.SetColumn(host, column);
        grid.Children.Add(host);
    }

    private static Border CreateSectionCard(Thickness padding, UIElement child)
    {
        return new Border
        {
            Padding = padding,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = ResolveBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ResolveBrush("BoardBorderStrongBrush"),
            Child = child,
        };
    }

    private static TextBlock CreateSectionTitleBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        };
    }

    private static TextBlock CreateSectionTitleBlock(double fontSize, Windows.UI.Text.FontWeight fontWeight)
    {
        return new TextBlock
        {
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.WrapWholeWords,
        };
    }

    private static TextBlock CreateFieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.82,
        };
    }

    private static TextBlock CreateHintBlock(string? text = null)
    {
        return new TextBlock
        {
            Text = text ?? string.Empty,
            Opacity = 0.68,
            TextWrapping = TextWrapping.WrapWholeWords,
        };
    }

    private static TextBox CreateEditorTextBox(double minHeight, bool isReadOnly)
    {
        return new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = minHeight,
            FontFamily = new FontFamily("Consolas"),
            IsReadOnly = isReadOnly,
        };
    }

    private static Brush? ResolveBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out object resource)
            ? resource as Brush
            : null;
    }

    public async void Render(string boardId, ManagedBoardConfigState? config)
    {
        currentBoardId = boardId ?? string.Empty;
        currentRawBoardJson = config?.RawBoardJson ?? "{}";
        BoardIdText.Text = string.IsNullOrWhiteSpace(currentBoardId) ? "Board id unavailable." : $"Board: {currentBoardId}";
        UiJsonTextBox.Text = PrettyJson(config?.RawUiJson ?? "{}");
        MetadataJsonTextBox.Text = PrettyJson(config?.RawMetadataJson ?? "{}");
        LayoutJsonTextBox.Text = PrettyJson(config?.RawLayoutJson ?? "null");
        SetThemePackSelection(BoardTheme.ResolveThemePackIdFromUiJson(config?.RawUiJson));
        StatusText.Text = "Edit the managed board UI config, metadata, and persisted canvas layout for this board.";
        TemplatePreviewText.Text = string.Empty;
        HostConfigPreviewTextBox.Text = string.Empty;
        EffectiveBoardConfigPreviewTextBox.Text = string.Empty;
        pendingTemplatePayloadJson = null;
        await LoadHostTemplateCatalogAsync();
        await LoadTemplatesAsync();
        PopulateAddBoardDefaults();
        await RefreshEffectiveBoardConfigPreviewAsync();
    }

    private async void OnSaveClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            StatusText.Text = "Board id unavailable.";
            return;
        }

        SaveButton.IsEnabled = false;
        StatusText.Text = "Saving board configuration...";
        try
        {
            App app = (App)Application.Current;
            string rawUiJson = MergeThemePackIntoUiJson(UiJsonTextBox.Text, ThemePackComboBox.SelectedItem as string);
            ManagedBoardConfigState saved = await app.BoardClient.SaveManagedBoardConfigAsync(
                currentBoardId,
                rawUiJson,
                MetadataJsonTextBox.Text,
                LayoutJsonTextBox.Text,
                currentRawBoardJson);
            currentRawBoardJson = saved.RawBoardJson;
            app.BoardStore.SetManagedBoardConfig(saved);
            UiJsonTextBox.Text = PrettyJson(saved.RawUiJson);
            MetadataJsonTextBox.Text = PrettyJson(saved.RawMetadataJson);
            LayoutJsonTextBox.Text = PrettyJson(saved.RawLayoutJson);
            ThemePackComboBox.SelectedItem = BoardTheme.ResolveThemePackIdFromUiJson(saved.RawUiJson);
            await app.HostConfigService.SyncBoardRecordAsync(currentBoardId, currentRawBoardJson);
            await RefreshEffectiveBoardConfigPreviewAsync();
            StatusText.Text = "Board configuration saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void OnAddBoardClick(object sender, RoutedEventArgs e)
    {
        App app = (App)Application.Current;
        AddBoardButton.IsEnabled = false;
        StatusText.Text = "Adding board...";

        try
        {
            var request = new ManagedBoardCreateRequest(
                AddBoardIdTextBox.Text,
                AddBoardLabelTextBox.Text,
                AddBoardPageTitleTextBox.Text,
                AddBoardPageSubtitleTextBox.Text,
                GetSelectedOrFallback(AddBoardAiComboBox, "copilot"),
                GetSelectedOrFallback(AddBoardAiWorkspaceTemplateComboBox, "default"),
                GetSelectedOrFallback(AddBoardUiTemplateComboBox, "default"),
                GetSelectedOrFallback(AddBoardRefsTemplateComboBox, "localfs-default"),
                TemplateComboBox.SelectedValue as string ?? string.Empty);

            ManagedBoardListEntry created = await app.BoardClient.AddManagedBoardAsync(request);
            if (!string.IsNullOrWhiteSpace(request.TemplateKey))
            {
                SampleTemplateEnvelope template = await app.BoardClient.GetSampleTemplateAsync(request.TemplateKey);
                await app.BoardClient.ApplyImportBoardAsync(created.Id, template.RawPayloadJson, "ingest", applyBoardMetadata: false);
            }

            await app.HostConfigService.SetupBoardWorkspaceAsync(created.Id, BuildBoardRecordJson(request));

            StatusText.Text = $"Created board {created.Label}.";
            PopulateAddBoardDefaults();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            AddBoardButton.IsEnabled = true;
        }
    }

    private async void OnImportBoardClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            StatusText.Text = "Board id unavailable.";
            return;
        }

        ImportBoardButton.IsEnabled = false;
        StatusText.Text = "Importing board dump...";
        try
        {
            string? json = await NativeFilePicker.PickJsonTextAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                StatusText.Text = "Import cancelled.";
                return;
            }

            App app = (App)Application.Current;
            await app.BoardClient.ApplyImportBoardAsync(currentBoardId, json, "replace", applyBoardMetadata: true);
            await ReloadCurrentBoardConfigAsync();
            await app.HostConfigService.SyncBoardRecordAsync(currentBoardId, currentRawBoardJson);
            await app.BoardClient.RefreshBoardAsync();
            StatusText.Text = "Board import applied.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            ImportBoardButton.IsEnabled = true;
        }
    }

    private async void OnExportBoardClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            StatusText.Text = "Board id unavailable.";
            return;
        }

        ExportBoardButton.IsEnabled = false;
        StatusText.Text = "Exporting board dump...";
        try
        {
            App app = (App)Application.Current;
            string json = await app.BoardClient.ExportBoardAsync(currentBoardId);
            bool saved = await NativeFilePicker.SaveJsonTextAsync($"{currentBoardId}-runtime-dump.json", json);
            StatusText.Text = saved ? "Board export saved." : "Export cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            ExportBoardButton.IsEnabled = true;
        }
    }

    private async void OnRefreshBoardClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            StatusText.Text = "Board id unavailable.";
            return;
        }

        RefreshBoardButton.IsEnabled = false;
        StatusText.Text = "Refreshing workspace bootstrap...";
        try
        {
            App app = (App)Application.Current;
            await app.HostConfigService.SetupBoardWorkspaceAsync(currentBoardId, currentRawBoardJson);
            await app.BoardClient.RefreshManagedBoardAsync(currentBoardId);
            await app.BoardClient.RefreshBoardAsync();
            await RefreshEffectiveBoardConfigPreviewAsync();
            StatusText.Text = "Workspace bootstrap refreshed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            RefreshBoardButton.IsEnabled = true;
        }
    }

    private void OnRunSmokeClick(object sender, RoutedEventArgs e)
    {
        MainPage.TryGetCurrent()?.ShowSmokeRunner();
    }

    private async void OnPreviewTemplateClick(object sender, RoutedEventArgs e)
    {
        string? templateKey = TemplateIngestComboBox.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(currentBoardId) || string.IsNullOrWhiteSpace(templateKey))
        {
            StatusText.Text = "Select a template to preview.";
            return;
        }

        PreviewTemplateButton.IsEnabled = false;
        StatusText.Text = "Preparing template preview...";
        try
        {
            App app = (App)Application.Current;
            SampleTemplateEnvelope template = await app.BoardClient.GetSampleTemplateAsync(templateKey);
            BoardImportPreview preview = await app.BoardClient.PreviewImportBoardAsync(currentBoardId, template.RawPayloadJson, "ingest");
            pendingTemplatePayloadJson = template.RawPayloadJson;
            TemplatePreviewText.Text = BuildTemplatePreviewText(template, preview);
            StatusText.Text = "Template preview ready.";
        }
        catch (Exception ex)
        {
            pendingTemplatePayloadJson = null;
            TemplatePreviewText.Text = string.Empty;
            StatusText.Text = ex.Message;
        }
        finally
        {
            PreviewTemplateButton.IsEnabled = true;
        }
    }

    private async void OnApplyTemplateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId) || string.IsNullOrWhiteSpace(pendingTemplatePayloadJson))
        {
            StatusText.Text = "Preview a template before ingesting it.";
            return;
        }

        ApplyTemplateButton.IsEnabled = false;
        StatusText.Text = "Ingesting template cards...";
        try
        {
            App app = (App)Application.Current;
            await app.BoardClient.ApplyImportBoardAsync(currentBoardId, pendingTemplatePayloadJson, "ingest", applyBoardMetadata: false);
            await app.BoardClient.RefreshBoardAsync();
            StatusText.Text = "Template cards ingested.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            ApplyTemplateButton.IsEnabled = true;
        }
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

    private async System.Threading.Tasks.Task LoadTemplatesAsync()
    {
        try
        {
            App app = (App)Application.Current;
            IReadOnlyList<SampleTemplateEntry> templates = await app.BoardClient.ListSampleTemplatesAsync();
            TemplateComboBox.ItemsSource = templates;
            TemplateIngestComboBox.ItemsSource = templates;
            if (templates.Count > 0)
            {
                TemplateComboBox.SelectedIndex = 0;
                TemplateIngestComboBox.SelectedIndex = 0;
            }
            TemplateHelpText.Text = templates.Count == 0
                ? "No sample templates are available."
                : "If selected, the template cards will be ingested into the newly created board.";
        }
        catch (Exception ex)
        {
            TemplateComboBox.ItemsSource = null;
            TemplateIngestComboBox.ItemsSource = null;
            TemplateHelpText.Text = ex.Message;
        }
    }

    private async System.Threading.Tasks.Task LoadHostTemplateCatalogAsync()
    {
        try
        {
            App app = (App)Application.Current;
            hostTemplateCatalog = await app.HostConfigService.LoadTemplateCatalogAsync();

            IReadOnlyList<string> assistantNames = hostTemplateCatalog.AssistantNames
                .Where(name => string.Equals(name, "copilot", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "foundry", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AddBoardAiComboBox.ItemsSource = assistantNames.Count > 0 ? assistantNames : hostTemplateCatalog.AssistantNames;
            AddBoardAiWorkspaceTemplateComboBox.ItemsSource = hostTemplateCatalog.AiWorkspaceTemplateNames;
            AddBoardUiTemplateComboBox.ItemsSource = hostTemplateCatalog.UiTemplateNames;
            AddBoardRefsTemplateComboBox.ItemsSource = hostTemplateCatalog.RefsTemplateNames;
            HostConfigPathsText.Text = $"Host config: {hostTemplateCatalog.HostConfigPath}{Environment.NewLine}Templates config: {hostTemplateCatalog.TemplatesConfigPath}{Environment.NewLine}Workspace setup script: {hostTemplateCatalog.SetupSingleAiWorkspaceScriptPath}";
            HostConfigPreviewTextBox.Text = PrettyJson(hostTemplateCatalog.RawHostSummaryJson);
        }
        catch (Exception ex)
        {
            hostTemplateCatalog = null;
            HostConfigPathsText.Text = ex.Message;
            HostConfigPreviewTextBox.Text = string.Empty;
            AddBoardAiComboBox.ItemsSource = new[] { "copilot", "foundry" };
            AddBoardAiWorkspaceTemplateComboBox.ItemsSource = Array.Empty<string>();
            AddBoardUiTemplateComboBox.ItemsSource = Array.Empty<string>();
            AddBoardRefsTemplateComboBox.ItemsSource = Array.Empty<string>();
        }
    }

    private void PopulateAddBoardDefaults()
    {
        SetComboValue(AddBoardAiComboBox, "copilot");
        SetComboValue(AddBoardAiWorkspaceTemplateComboBox, "default");
        SetComboValue(AddBoardUiTemplateComboBox, "default");
        SetComboValue(AddBoardRefsTemplateComboBox, "localfs-default");
        AddBoardIdTextBox.Text = string.Empty;
        AddBoardLabelTextBox.Text = string.Empty;
        AddBoardPageTitleTextBox.Text = string.Empty;
        AddBoardPageSubtitleTextBox.Text = string.Empty;
    }

    private async System.Threading.Tasks.Task ReloadCurrentBoardConfigAsync()
    {
        App app = (App)Application.Current;
        ManagedBoardConfigState? saved = await app.BoardClient.GetManagedBoardConfigAsync(currentBoardId);
        if (saved is null)
        {
            return;
        }

        currentRawBoardJson = saved.RawBoardJson;
        app.BoardStore.SetManagedBoardConfig(saved);
        UiJsonTextBox.Text = PrettyJson(saved.RawUiJson);
        MetadataJsonTextBox.Text = PrettyJson(saved.RawMetadataJson);
        LayoutJsonTextBox.Text = PrettyJson(saved.RawLayoutJson);
        SetThemePackSelection(BoardTheme.ResolveThemePackIdFromUiJson(saved.RawUiJson));
        await RefreshEffectiveBoardConfigPreviewAsync();
    }

    private async void OnPreviewEffectiveConfigClick(object sender, RoutedEventArgs e)
    {
        PreviewEffectiveConfigButton.IsEnabled = false;
        try
        {
            await RefreshEffectiveBoardConfigPreviewAsync();
            StatusText.Text = "Effective board config preview updated.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            PreviewEffectiveConfigButton.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task RefreshEffectiveBoardConfigPreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            EffectiveBoardConfigPreviewTextBox.Text = string.Empty;
            return;
        }

        App app = (App)Application.Current;
        EffectiveBoardConfigPreviewTextBox.Text = await app.HostConfigService.ResolveBoardConfigJsonAsync(currentBoardId, currentRawBoardJson);
    }

    private void OnThemePackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressThemeSelectionSync)
        {
            return;
        }

        UiJsonTextBox.Text = PrettyJson(MergeThemePackIntoUiJson(UiJsonTextBox.Text, ThemePackComboBox.SelectedItem as string));
    }

    private void SetThemePackSelection(string? themePackId)
    {
        suppressThemeSelectionSync = true;
        ThemePackComboBox.SelectedItem = BoardTheme.NormalizeThemePackId(themePackId);
        suppressThemeSelectionSync = false;
    }

    private static string MergeThemePackIntoUiJson(string rawUiJson, string? themePackId)
    {
        string normalizedThemePackId = BoardTheme.NormalizeThemePackId(themePackId);
        JsonObject ui = ParseObject(rawUiJson);
        JsonObject theme = ui["theme"] as JsonObject ?? new JsonObject();
        theme["id"] = normalizedThemePackId;
        ui["theme"] = theme;
        return ui.ToJsonString(PrettyJsonOptions);
    }

    private static JsonObject ParseObject(string? rawJson)
    {
        if (!string.IsNullOrWhiteSpace(rawJson)
            && JsonNode.Parse(rawJson) is JsonObject parsed)
        {
            return parsed;
        }

        return new JsonObject();
    }

    private static void SetComboValue(ComboBox comboBox, string desiredValue)
    {
        if (comboBox.ItemsSource is IEnumerable<string> values
            && values.Any(value => string.Equals(value, desiredValue, StringComparison.OrdinalIgnoreCase)))
        {
            comboBox.SelectedItem = values.First(value => string.Equals(value, desiredValue, StringComparison.OrdinalIgnoreCase));
            return;
        }

        comboBox.SelectedItem = desiredValue;
    }

    private string GetSelectedOrFallback(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : fallback;
    }

    private static string BuildBoardRecordJson(ManagedBoardCreateRequest request)
    {
        JsonObject root = new()
        {
            ["id"] = request.BoardId,
            ["label"] = request.Label,
            ["ai"] = request.Ai,
            ["aiWorkspaceTemplate"] = request.AiWorkspaceTemplate,
            ["uiTemplate"] = request.UiTemplate,
            ["refsTemplate"] = request.RefsTemplate,
            ["metadata"] = new JsonObject
            {
                ["pageTitle"] = request.PageTitle,
                ["pageSubtitle"] = request.PageSubtitle,
            }
        };
        return root.ToJsonString(PrettyJsonOptions);
    }

    private static string BuildTemplatePreviewText(SampleTemplateEnvelope template, BoardImportPreview preview)
    {
        var lines = new List<string>
        {
            $"Template: {template.Label}",
            $"Replace: {preview.ReplaceIds.Count}",
            $"Add: {preview.AddIds.Count}",
            $"Invalid: {preview.InvalidCards.Count}"
        };

        if (preview.ReplaceIds.Count > 0)
        {
            lines.Add($"Replace ids: {string.Join(", ", preview.ReplaceIds)}");
        }

        if (preview.AddIds.Count > 0)
        {
            lines.Add($"Add ids: {string.Join(", ", preview.AddIds)}");
        }

        if (preview.InvalidCards.Count > 0)
        {
            lines.Add("Invalid cards:");
            lines.AddRange(preview.InvalidCards.Select(card => $"- {ValueOrFallback(card.Id, "(missing id)")}: {string.Join("; ", card.Issues)}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ValueOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
