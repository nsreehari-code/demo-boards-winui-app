using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class AppConfigModal : UserControl
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };
    private const int DefaultRefreshAllIntervalSeconds = 30 * 60;

    private readonly TextBlock BoardIdText;
    private readonly Button ClosePanelButton;
    private readonly Button OpenAddBoardButton;
    private readonly Button RunSmokeButton;

    private readonly TextBox PageTitleTextBox;
    private readonly TextBox PageSubtitleTextBox;
    private readonly TextBox RefreshIntervalMinutesTextBox;
    private readonly ComboBox ThemePackComboBox;
    private readonly ComboBox UiTemplateComboBox;
    private readonly Button SaveButton;
    private readonly TextBlock PageDetailsStatusText;

    private readonly TextBlock HostConfigPathsText;
    private readonly Button PreviewEffectiveConfigButton;
    private readonly Button HideEffectiveConfigButton;
    private readonly StackPanel HostConfigPreviewStack;
    private readonly TextBox HostConfigPreviewTextBox;
    private readonly TextBox EffectiveBoardConfigPreviewTextBox;
    private readonly TextBlock HostConfigStatusText;

    private readonly Button ImportBoardButton;
    private readonly Button ExportBoardButton;
    private readonly Button RefreshBoardButton;
    private readonly TextBlock ImportExportStatusText;

    private readonly ComboBox TemplateIngestComboBox;
    private readonly Button PreviewTemplateButton;
    private readonly Button ApplyTemplateButton;
    private readonly TextBlock TemplatePreviewText;
    private readonly TextBlock TemplateIngestStatusText;

    private readonly GlobalModal AddBoardModalHost;
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
    private readonly Button CancelAddBoardButton;
    private readonly TextBlock TemplateHelpText;
    private readonly TextBlock AddBoardStatusText;
    private readonly UIElement AddBoardModalContent;

    private string currentBoardId = string.Empty;
    private string currentRawBoardJson = "{}";
    private string currentRawUiJson = "{}";
    private string currentRawMetadataJson = "{}";
    private string currentRawLayoutJson = "null";
    private string? pendingTemplatePayloadJson;
    private WinUiHostTemplateCatalog? hostTemplateCatalog;

    public event EventHandler? CloseRequested;

    public AppConfigModal()
    {
        BoardIdText = CreateSectionTitleBlock(20, FontWeights.SemiBold);
        ClosePanelButton = new Button { Content = "Close" };
        OpenAddBoardButton = new Button { Content = "New board" };
        RunSmokeButton = new Button { Content = "Run tests" };

        PageTitleTextBox = new TextBox { PlaceholderText = "Live" };
        PageSubtitleTextBox = new TextBox { PlaceholderText = "Live operational intelligence for agent workflows" };
        RefreshIntervalMinutesTextBox = new TextBox { PlaceholderText = "30" };
        ThemePackComboBox = new ComboBox();
        UiTemplateComboBox = new ComboBox { PlaceholderText = "UI template" };
        SaveButton = new Button { Content = "Save" };
        PageDetailsStatusText = CreateStatusBlock();

        HostConfigPathsText = CreateHintBlock();
        PreviewEffectiveConfigButton = new Button { Content = "Preview effective board config" };
        HideEffectiveConfigButton = new Button { Content = "Hide preview", Visibility = Visibility.Collapsed };
        HostConfigPreviewStack = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        HostConfigPreviewTextBox = CreateEditorTextBox(120, true);
        EffectiveBoardConfigPreviewTextBox = CreateEditorTextBox(180, true);
        HostConfigStatusText = CreateStatusBlock();

        ImportBoardButton = new Button { Content = "Import board" };
        ExportBoardButton = new Button { Content = "Export board" };
        RefreshBoardButton = new Button { Content = "Refresh workspace bootstrap" };
        ImportExportStatusText = CreateStatusBlock();

        TemplateIngestComboBox = new ComboBox
        {
            PlaceholderText = "Select template",
            DisplayMemberPath = nameof(SampleTemplateEntry.Label),
            SelectedValuePath = nameof(SampleTemplateEntry.Key)
        };
        PreviewTemplateButton = new Button { Content = "Preview ingest" };
        ApplyTemplateButton = new Button { Content = "Ingest template" };
        TemplatePreviewText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.WrapWholeWords
        };
        TemplateIngestStatusText = CreateStatusBlock();

        AddBoardModalHost = new GlobalModal();
        AddBoardIdTextBox = new TextBox { PlaceholderText = "board-id" };
        AddBoardLabelTextBox = new TextBox { PlaceholderText = "Label" };
        AddBoardPageTitleTextBox = new TextBox { PlaceholderText = "Page Title" };
        AddBoardPageSubtitleTextBox = new TextBox { PlaceholderText = "Page Subtitle" };
        AddBoardAiComboBox = new ComboBox { PlaceholderText = "AI" };
        AddBoardAiWorkspaceTemplateComboBox = new ComboBox { PlaceholderText = "AI workspace template" };
        AddBoardUiTemplateComboBox = new ComboBox { PlaceholderText = "UI template" };
        AddBoardRefsTemplateComboBox = new ComboBox { PlaceholderText = "Refs template" };
        TemplateComboBox = new ComboBox
        {
            PlaceholderText = "Optional card template",
            DisplayMemberPath = nameof(SampleTemplateEntry.Label),
            SelectedValuePath = nameof(SampleTemplateEntry.Key)
        };
        AddBoardButton = new Button { Content = "Add board" };
        CancelAddBoardButton = new Button { Content = "Cancel" };
        TemplateHelpText = CreateHintBlock();
        AddBoardStatusText = CreateStatusBlock();
        AddBoardModalContent = BuildAddBoardModalContent();

        ThemePackComboBox.ItemsSource = BoardTheme.ThemePackIds;

        ClosePanelButton.Click += OnClosePanelClick;
        OpenAddBoardButton.Click += OnOpenAddBoardClick;
        RunSmokeButton.Click += OnRunSmokeClick;
        SaveButton.Click += OnSaveClick;
        PreviewEffectiveConfigButton.Click += OnPreviewEffectiveConfigClick;
        HideEffectiveConfigButton.Click += OnHideEffectiveConfigClick;
        ImportBoardButton.Click += OnImportBoardClick;
        ExportBoardButton.Click += OnExportBoardClick;
        RefreshBoardButton.Click += OnRefreshBoardClick;
        PreviewTemplateButton.Click += OnPreviewTemplateClick;
        ApplyTemplateButton.Click += OnApplyTemplateClick;
        AddBoardButton.Click += OnAddBoardClick;
        CancelAddBoardButton.Click += OnCancelAddBoardClick;
        AddBoardModalHost.CloseRequested += OnAddBoardModalCloseRequested;

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var sections = new StackPanel { Spacing = 14 };

        sections.Children.Add(BuildPanelHeaderCard());

        sections.Children.Add(CreateSectionCard(new Thickness(16, 14, 16, 14), new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateFieldLabel("Board"),
                BoardIdText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { OpenAddBoardButton, RunSmokeButton }
                }
            }
        }));

        sections.Children.Add(CreateSectionCard(new Thickness(14), BuildPageDetailsSection()));
        sections.Children.Add(CreateSectionCard(new Thickness(14), BuildHostConfigSection()));
        sections.Children.Add(CreateSectionCard(new Thickness(14), BuildImportExportSection()));
        sections.Children.Add(CreateSectionCard(new Thickness(14), BuildTemplateIngestSection()));

        var root = new Grid();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = sections,
        });
        root.Children.Add(AddBoardModalHost);
        return root;
    }

    private UIElement BuildPanelHeaderCard()
    {
        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                CreateSectionTitleBlock("Board Settings"),
                CreateHintBlock("Edit board details from this right rail without dropping into a diagnostics-heavy modal flow.")
            }
        });
        Grid.SetColumn(ClosePanelButton, 1);
        headerGrid.Children.Add(ClosePanelButton);

        return CreateSectionCard(new Thickness(16, 14, 16, 14), headerGrid);
    }

    private UIElement BuildPageDetailsSection()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(CreateSectionTitleBlock("Page Details"));
        Grid.SetColumn(SaveButton, 1);
        header.Children.Add(SaveButton);

        var grid = CreateTwoColumnGrid(3);
        AddLabeledField(grid, 0, 0, "Page title", PageTitleTextBox);
        AddLabeledField(grid, 0, 1, "Page subtitle", PageSubtitleTextBox);
        AddLabeledField(grid, 1, 0, "Refresh interval (minutes)", RefreshIntervalMinutesTextBox);
        AddLabeledField(grid, 1, 1, "Theme pack", ThemePackComboBox);
        AddLabeledField(grid, 2, 0, "UI template", UiTemplateComboBox);

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                header,
                CreateHintBlock("Edit the board title, subtitle, refresh cadence, theme pack, and UI template without raw JSON editing."),
                grid,
                PageDetailsStatusText,
            }
        };
    }

    private UIElement BuildHostConfigSection()
    {
        HostConfigPreviewStack.Children.Clear();
        HostConfigPreviewStack.Children.Add(CreateFieldLabel("Resolved host summary"));
        HostConfigPreviewStack.Children.Add(HostConfigPreviewTextBox);
        HostConfigPreviewStack.Children.Add(CreateFieldLabel("Resolved effective board config"));
        HostConfigPreviewStack.Children.Add(EffectiveBoardConfigPreviewTextBox);

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Host Runtime Config"),
                CreateHintBlock("Use this only when you need to inspect resolved host wiring."),
                HostConfigPathsText,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { PreviewEffectiveConfigButton, HideEffectiveConfigButton } },
                HostConfigPreviewStack,
                HostConfigStatusText,
            }
        };
    }

    private UIElement BuildImportExportSection()
    {
        var grid = CreateTwoColumnGrid(2);
        grid.RowSpacing = 8;
        grid.ColumnSpacing = 8;
        PlaceInGrid(grid, ImportBoardButton, 0, 0);
        PlaceInGrid(grid, ExportBoardButton, 0, 1);
        PlaceInGrid(grid, RefreshBoardButton, 1, 0);

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Board Import / Export"),
                CreateHintBlock("Move runtime dumps in and out, or refresh the workspace bootstrap state."),
                grid,
                ImportExportStatusText,
            }
        };
    }

    private UIElement BuildTemplateIngestSection()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        PlaceInGrid(grid, TemplateIngestComboBox, 0, 0);
        PlaceInGrid(grid, PreviewTemplateButton, 0, 1);
        PlaceInGrid(grid, ApplyTemplateButton, 0, 2);

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateSectionTitleBlock("Template Card Ingest"),
                CreateHintBlock("Preview the card delta first, then ingest into the current board when the result looks right."),
                grid,
                TemplateIngestStatusText,
                TemplatePreviewText,
            }
        };
    }

    private UIElement BuildAddBoardModalContent()
    {
        var grid = CreateTwoColumnGrid(5);
        AddLabeledField(grid, 0, 0, "Board id", AddBoardIdTextBox);
        AddLabeledField(grid, 0, 1, "Label", AddBoardLabelTextBox);
        AddLabeledField(grid, 1, 0, "Page title", AddBoardPageTitleTextBox);
        AddLabeledField(grid, 1, 1, "Page subtitle", AddBoardPageSubtitleTextBox);
        AddLabeledField(grid, 2, 0, "AI", AddBoardAiComboBox);
        AddLabeledField(grid, 2, 1, "AI workspace template", AddBoardAiWorkspaceTemplateComboBox);
        AddLabeledField(grid, 3, 0, "UI template", AddBoardUiTemplateComboBox);
        AddLabeledField(grid, 3, 1, "Refs template", AddBoardRefsTemplateComboBox);
        AddLabeledField(grid, 4, 0, "Optional card template", TemplateComboBox);

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreateHintBlock("Create a new managed board and optionally seed it from a template."),
                grid,
                TemplateHelpText,
                AddBoardStatusText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { CancelAddBoardButton, AddBoardButton }
                }
            }
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

    private static void AddLabeledField(Grid grid, int row, int column, string label, Control control)
    {
        var host = new StackPanel { Spacing = 6 };
        host.Children.Add(CreateFieldLabel(label));
        host.Children.Add(control);
        Grid.SetRow(host, row);
        Grid.SetColumn(host, column);
        grid.Children.Add(host);
    }

    private static void PlaceInGrid(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
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

    private static TextBlock CreateStatusBlock()
    {
        return new TextBlock
        {
            Opacity = 0.82,
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
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
        currentRawUiJson = config?.RawUiJson ?? "{}";
        currentRawMetadataJson = config?.RawMetadataJson ?? "{}";
        currentRawLayoutJson = string.IsNullOrWhiteSpace(config?.RawLayoutJson) ? "null" : config!.RawLayoutJson;

        BoardIdText.Text = string.IsNullOrWhiteSpace(currentBoardId) ? "Board id unavailable." : $"Board: {currentBoardId}";
        ClearStatus(AddBoardStatusText);
        ClearStatus(PageDetailsStatusText);
        ClearStatus(HostConfigStatusText);
        ClearStatus(ImportExportStatusText);
        ClearStatus(TemplateIngestStatusText);
        TemplatePreviewText.Text = string.Empty;
        HostConfigPreviewTextBox.Text = string.Empty;
        EffectiveBoardConfigPreviewTextBox.Text = string.Empty;
        SetHostConfigPreviewVisibility(false);
        pendingTemplatePayloadJson = null;

        await LoadHostTemplateCatalogAsync();
        await LoadTemplatesAsync();
        PopulateAddBoardDefaults();
        PopulatePageDetailsFields();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            SetStatus(PageDetailsStatusText, "Board id unavailable.", isError: true);
            return;
        }

        string pageTitle = PageTitleTextBox.Text.Trim();
        string pageSubtitle = PageSubtitleTextBox.Text.Trim();
        string refreshMinutesText = RefreshIntervalMinutesTextBox.Text.Trim();
        string themePackId = ThemePackComboBox.SelectedItem as string ?? BoardTheme.DefaultThemePackId;
        string uiTemplate = UiTemplateComboBox.SelectedItem as string ?? "default";

        if (pageTitle.Length == 0 || pageSubtitle.Length == 0 || refreshMinutesText.Length == 0 || uiTemplate.Length == 0)
        {
            SetStatus(PageDetailsStatusText, "All page detail fields are required.", isError: true);
            return;
        }

        if (!int.TryParse(refreshMinutesText, out int refreshMinutes) || refreshMinutes <= 0)
        {
            SetStatus(PageDetailsStatusText, "Refresh interval must be a positive number of minutes.", isError: true);
            return;
        }

        SaveButton.IsEnabled = false;
        SetStatus(PageDetailsStatusText, "Saving page details...");
        try
        {
            App app = (App)Application.Current;
            string rawUiJson = MergeThemePackIntoUiJson(currentRawUiJson, themePackId);
            string rawMetadataJson = BuildUpdatedMetadataJson(currentRawMetadataJson, pageTitle, pageSubtitle, refreshMinutes);
            string updatedBoardRecordJson = BuildUpdatedBoardRecordJson(currentRawBoardJson, uiTemplate);

            ManagedBoardConfigState saved = await app.BoardClient.SaveManagedBoardConfigAsync(
                currentBoardId,
                rawUiJson,
                rawMetadataJson,
                currentRawLayoutJson,
                updatedBoardRecordJson);

            currentRawBoardJson = saved.RawBoardJson;
            currentRawUiJson = saved.RawUiJson;
            currentRawMetadataJson = saved.RawMetadataJson;
            currentRawLayoutJson = saved.RawLayoutJson;
            app.BoardStore.SetManagedBoardConfig(saved);
            PopulatePageDetailsFields();
            await app.HostConfigService.SyncBoardRecordAsync(currentBoardId, currentRawBoardJson);
            if (HostConfigPreviewStack.Visibility == Visibility.Visible)
            {
                await RefreshEffectiveBoardConfigPreviewAsync();
            }
            SetStatus(PageDetailsStatusText, "Saved.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus(PageDetailsStatusText, ex.Message, isError: true);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnOpenAddBoardClick(object sender, RoutedEventArgs e)
    {
        PopulateAddBoardDefaults();
        ClearStatus(AddBoardStatusText);
        AddBoardModalHost.Show("Add board", AddBoardModalContent);
    }

    private void OnCancelAddBoardClick(object sender, RoutedEventArgs e)
    {
        AddBoardModalHost.Hide();
    }

    private void OnClosePanelClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHideEffectiveConfigClick(object sender, RoutedEventArgs e)
    {
        SetHostConfigPreviewVisibility(false);
        ClearStatus(HostConfigStatusText);
    }

    private void OnAddBoardModalCloseRequested(object? sender, EventArgs e)
    {
        AddBoardModalHost.Hide();
    }

    private async void OnAddBoardClick(object sender, RoutedEventArgs e)
    {
        App app = (App)Application.Current;
        AddBoardButton.IsEnabled = false;
        CancelAddBoardButton.IsEnabled = false;
        SetStatus(AddBoardStatusText, "Adding board...");

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
            AddBoardModalHost.Hide();
        }
        catch (Exception ex)
        {
            SetStatus(AddBoardStatusText, ex.Message, isError: true);
        }
        finally
        {
            AddBoardButton.IsEnabled = true;
            CancelAddBoardButton.IsEnabled = true;
        }
    }

    private async void OnImportBoardClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            SetStatus(ImportExportStatusText, "Board id unavailable.", isError: true);
            return;
        }

        ImportBoardButton.IsEnabled = false;
        SetStatus(ImportExportStatusText, "Importing board dump...");
        try
        {
            string? json = await NativeFilePicker.PickJsonTextAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                SetStatus(ImportExportStatusText, "Import cancelled.");
                return;
            }

            App app = (App)Application.Current;
            await app.BoardClient.ApplyImportBoardAsync(currentBoardId, json, "replace", applyBoardMetadata: true);
            await ReloadCurrentBoardConfigAsync();
            await app.HostConfigService.SyncBoardRecordAsync(currentBoardId, currentRawBoardJson);
            await app.BoardClient.RefreshBoardAsync();
            SetStatus(ImportExportStatusText, "Board import applied.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus(ImportExportStatusText, ex.Message, isError: true);
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
            SetStatus(ImportExportStatusText, "Board id unavailable.", isError: true);
            return;
        }

        ExportBoardButton.IsEnabled = false;
        SetStatus(ImportExportStatusText, "Exporting board dump...");
        try
        {
            App app = (App)Application.Current;
            string json = await app.BoardClient.ExportBoardAsync(currentBoardId);
            bool saved = await NativeFilePicker.SaveJsonTextAsync($"{currentBoardId}-runtime-dump.json", json);
            SetStatus(ImportExportStatusText, saved ? "Board export saved." : "Export cancelled.", isSuccess: saved);
        }
        catch (Exception ex)
        {
            SetStatus(ImportExportStatusText, ex.Message, isError: true);
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
            SetStatus(ImportExportStatusText, "Board id unavailable.", isError: true);
            return;
        }

        RefreshBoardButton.IsEnabled = false;
        SetStatus(ImportExportStatusText, "Refreshing workspace bootstrap...");
        try
        {
            App app = (App)Application.Current;
            await app.HostConfigService.SetupBoardWorkspaceAsync(currentBoardId, currentRawBoardJson);
            await app.BoardClient.RefreshManagedBoardAsync(currentBoardId);
            await app.BoardClient.RefreshBoardAsync();
            await RefreshEffectiveBoardConfigPreviewAsync();
            SetStatus(ImportExportStatusText, "Workspace bootstrap refreshed.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus(ImportExportStatusText, ex.Message, isError: true);
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
            SetStatus(TemplateIngestStatusText, "Select a template to preview.", isError: true);
            return;
        }

        PreviewTemplateButton.IsEnabled = false;
        SetStatus(TemplateIngestStatusText, "Preparing template preview...");
        try
        {
            App app = (App)Application.Current;
            SampleTemplateEnvelope template = await app.BoardClient.GetSampleTemplateAsync(templateKey);
            BoardImportPreview preview = await app.BoardClient.PreviewImportBoardAsync(currentBoardId, template.RawPayloadJson, "ingest");
            pendingTemplatePayloadJson = template.RawPayloadJson;
            TemplatePreviewText.Text = BuildTemplatePreviewText(template, preview);
            SetStatus(TemplateIngestStatusText, "Template preview ready.", isSuccess: true);
        }
        catch (Exception ex)
        {
            pendingTemplatePayloadJson = null;
            TemplatePreviewText.Text = string.Empty;
            SetStatus(TemplateIngestStatusText, ex.Message, isError: true);
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
            SetStatus(TemplateIngestStatusText, "Preview a template before ingesting it.", isError: true);
            return;
        }

        ApplyTemplateButton.IsEnabled = false;
        SetStatus(TemplateIngestStatusText, "Ingesting template cards...");
        try
        {
            App app = (App)Application.Current;
            await app.BoardClient.ApplyImportBoardAsync(currentBoardId, pendingTemplatePayloadJson, "ingest", applyBoardMetadata: false);
            await app.BoardClient.RefreshBoardAsync();
            SetStatus(TemplateIngestStatusText, "Template cards ingested.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus(TemplateIngestStatusText, ex.Message, isError: true);
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
            var addBoardTemplates = new List<SampleTemplateEntry> { new(string.Empty, "No template", string.Empty) };
            addBoardTemplates.AddRange(templates);
            TemplateComboBox.ItemsSource = addBoardTemplates;
            TemplateIngestComboBox.ItemsSource = templates;
            TemplateComboBox.SelectedIndex = 0;
            if (templates.Count > 0)
            {
                TemplateIngestComboBox.SelectedIndex = 0;
            }

            TemplateHelpText.Text = templates.Count == 0
                ? "No sample templates are available."
                : "If selected, the template cards will be ingested into the newly created board.";
        }
        catch (Exception ex)
        {
            TemplateComboBox.ItemsSource = new[] { new SampleTemplateEntry(string.Empty, "No template", string.Empty) };
            TemplateComboBox.SelectedIndex = 0;
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
            UiTemplateComboBox.ItemsSource = hostTemplateCatalog.UiTemplateNames;
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
            UiTemplateComboBox.ItemsSource = Array.Empty<string>();
        }
    }

    private void PopulateAddBoardDefaults()
    {
        SetComboValue(AddBoardAiComboBox, "copilot");
        SetComboValue(AddBoardAiWorkspaceTemplateComboBox, "default");
        SetComboValue(AddBoardUiTemplateComboBox, "default");
        SetComboValue(AddBoardRefsTemplateComboBox, "localfs-default");
        TemplateComboBox.SelectedIndex = 0;
        AddBoardIdTextBox.Text = string.Empty;
        AddBoardLabelTextBox.Text = string.Empty;
        AddBoardPageTitleTextBox.Text = string.Empty;
        AddBoardPageSubtitleTextBox.Text = string.Empty;
    }

    private void PopulatePageDetailsFields()
    {
        PageDetailsDraft draft = ResolvePageDetailsDraft(currentBoardId, currentRawBoardJson, currentRawUiJson, currentRawMetadataJson);
        PageTitleTextBox.Text = draft.PageTitle;
        PageSubtitleTextBox.Text = draft.PageSubtitle;
        RefreshIntervalMinutesTextBox.Text = draft.RefreshIntervalMinutes;
        SetComboValue(ThemePackComboBox, draft.ThemePackId);
        SetComboValue(UiTemplateComboBox, draft.UiTemplate);
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
        currentRawUiJson = saved.RawUiJson;
        currentRawMetadataJson = saved.RawMetadataJson;
        currentRawLayoutJson = saved.RawLayoutJson;
        app.BoardStore.SetManagedBoardConfig(saved);
        PopulatePageDetailsFields();
        if (HostConfigPreviewStack.Visibility == Visibility.Visible)
        {
            await RefreshEffectiveBoardConfigPreviewAsync();
        }
    }

    private async void OnPreviewEffectiveConfigClick(object sender, RoutedEventArgs e)
    {
        PreviewEffectiveConfigButton.IsEnabled = false;
        HideEffectiveConfigButton.IsEnabled = false;
        try
        {
            SetHostConfigPreviewVisibility(true);
            await RefreshEffectiveBoardConfigPreviewAsync();
            SetStatus(HostConfigStatusText, "Effective board config preview updated.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus(HostConfigStatusText, ex.Message, isError: true);
        }
        finally
        {
            PreviewEffectiveConfigButton.IsEnabled = true;
            HideEffectiveConfigButton.IsEnabled = true;
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

    private void SetHostConfigPreviewVisibility(bool visible)
    {
        HostConfigPreviewStack.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        HideEffectiveConfigButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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

    private static string BuildUpdatedMetadataJson(string currentRawMetadataJson, string pageTitle, string pageSubtitle, int refreshMinutes)
    {
        JsonObject metadata = ParseObject(currentRawMetadataJson);
        metadata["pageTitle"] = pageTitle;
        metadata["pageSubtitle"] = pageSubtitle;
        metadata["refreshAllIntervalSeconds"] = Math.Max(1, refreshMinutes) * 60;
        return metadata.ToJsonString(PrettyJsonOptions);
    }

    private static string BuildUpdatedBoardRecordJson(string currentRawBoardJson, string uiTemplate)
    {
        JsonObject board = ParseObject(currentRawBoardJson);
        board["uiTemplate"] = uiTemplate;
        return board.ToJsonString(PrettyJsonOptions);
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

    private static void SetStatus(TextBlock textBlock, string message, bool isError = false, bool isSuccess = false)
    {
        textBlock.Text = message;
        textBlock.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        textBlock.Foreground = isError
            ? new SolidColorBrush(Colors.IndianRed)
            : isSuccess
                ? new SolidColorBrush(Colors.SeaGreen)
                : ResolveBrush("BoardTextMutedBrush") ?? new SolidColorBrush(Colors.DimGray);
    }

    private static void ClearStatus(TextBlock textBlock)
    {
        textBlock.Text = string.Empty;
        textBlock.Visibility = Visibility.Collapsed;
        textBlock.Foreground = ResolveBrush("BoardTextMutedBrush") ?? new SolidColorBrush(Colors.DimGray);
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

    private static PageDetailsDraft ResolvePageDetailsDraft(string boardId, string rawBoardJson, string rawUiJson, string rawMetadataJson)
    {
        string title = ResolveMetadataString(rawMetadataJson, "pageTitle", string.IsNullOrWhiteSpace(boardId) ? "Demo Boards" : boardId);
        string subtitle = ResolveMetadataString(rawMetadataJson, "pageSubtitle", "Embedded board workspace");
        string refreshIntervalMinutes = ResolveRefreshIntervalMinutes(rawMetadataJson);
        string themePackId = BoardTheme.ResolveThemePackIdFromUiJson(rawUiJson);
        string uiTemplate = ResolveUiTemplate(rawBoardJson);
        return new PageDetailsDraft(title, subtitle, refreshIntervalMinutes, themePackId, uiTemplate);
    }

    private static string ResolveMetadataString(string rawMetadataJson, string propertyName, string fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawMetadataJson);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static string ResolveRefreshIntervalMinutes(string rawMetadataJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawMetadataJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("refreshAllIntervalSeconds", out JsonElement secondsElement)
                && secondsElement.ValueKind == JsonValueKind.Number
                && secondsElement.TryGetInt32(out int seconds)
                && seconds > 0)
            {
                return Math.Max(1, seconds / 60).ToString();
            }

            if (root.TryGetProperty("refreshAllIntervalMs", out JsonElement millisecondsElement)
                && millisecondsElement.ValueKind == JsonValueKind.Number
                && millisecondsElement.TryGetInt32(out int milliseconds)
                && milliseconds > 0)
            {
                return Math.Max(1, milliseconds / 60000).ToString();
            }
        }
        catch
        {
        }

        return Math.Max(1, DefaultRefreshAllIntervalSeconds / 60).ToString();
    }

    private static string ResolveUiTemplate(string rawBoardJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawBoardJson);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("uiTemplate", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }
        catch
        {
        }

        return "default";
    }

    private static string ValueOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed record PageDetailsDraft(
        string PageTitle,
        string PageSubtitle,
        string RefreshIntervalMinutes,
        string ThemePackId,
        string UiTemplate);
}
