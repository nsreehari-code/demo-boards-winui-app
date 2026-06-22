using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using DemoBoards_WinUI.Config;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed partial class AppConfigModal : UserControl
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    private string currentBoardId = string.Empty;
    private string currentRawBoardJson = "{}";
    private string? pendingTemplatePayloadJson;
    private bool suppressThemeSelectionSync;
    private WinUiHostTemplateCatalog? hostTemplateCatalog;

    public AppConfigModal()
    {
        InitializeComponent();
        ThemePackComboBox.ItemsSource = BoardTheme.ThemePackIds;
    }

    public async void Render(string boardId, ManagedBoardConfigState? config)
    {
        currentBoardId = boardId ?? string.Empty;
        currentRawBoardJson = config?.RawBoardJson ?? "{}";
        BoardIdText.Text = string.IsNullOrWhiteSpace(currentBoardId) ? "Board id unavailable." : $"Board: {currentBoardId}";
        UiJsonTextBox.Text = PrettyJson(config?.RawUiJson ?? "{}");
        MetadataJsonTextBox.Text = PrettyJson(config?.RawMetadataJson ?? "{}");
        LayoutJsonTextBox.Text = PrettyJson(config?.RawLayoutJson ?? "null");
        ThemePackComboBox.SelectedItem = BoardTheme.ResolveThemePackIdFromUiJson(config?.RawUiJson);
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
        ThemePackComboBox.SelectedItem = BoardTheme.ResolveThemePackIdFromUiJson(saved.RawUiJson);
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
