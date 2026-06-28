using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.Controls.Registry.BoardConfig;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorAppConfigModalProps(string BoardId, ManagedBoardConfigState? Config, Action CloseAction, Action? OnRunTests = null);

public sealed class ReactorAppConfigModalComponent : HookComponent<ReactorAppConfigModalProps>
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };
    private const int DefaultRefreshAllIntervalSeconds = 30 * 60;

    public override Element Render()
    {
        var (rawBoardJson, setRawBoardJson) = UseState("{}");
        var (rawUiJson, setRawUiJson) = UseState("{}");
        var (rawMetadataJson, setRawMetadataJson) = UseState("{}");
        var (rawLayoutJson, setRawLayoutJson) = UseState("null");

        var (pageTitle, setPageTitle) = UseState(string.Empty);
        var (pageSubtitle, setPageSubtitle) = UseState(string.Empty);
        var (refreshIntervalMinutes, setRefreshIntervalMinutes) = UseState(Math.Max(1, DefaultRefreshAllIntervalSeconds / 60).ToString());
        var (themePackId, setThemePackId) = UseState(BoardTheme.DefaultThemePackId);
        var (uiTemplate, setUiTemplate) = UseState("default");

        var (pageStatus, setPageStatus) = UseState(StatusMessage.Empty);
        var (hostStatus, setHostStatus) = UseState(StatusMessage.Empty);
        var (importExportStatus, setImportExportStatus) = UseState(StatusMessage.Empty);
        var (templateStatus, setTemplateStatus) = UseState(StatusMessage.Empty);
        var (addBoardStatus, setAddBoardStatus) = UseState(StatusMessage.Empty);

        var (hostConfigPathsText, setHostConfigPathsText) = UseState(string.Empty);
        var (hostConfigPreviewText, setHostConfigPreviewText) = UseState(string.Empty);
        var (effectiveBoardConfigPreviewText, setEffectiveBoardConfigPreviewText) = UseState(string.Empty);
        var (showHostPreview, setShowHostPreview) = UseState(false);
        var (confirmRuntimeImport, setConfirmRuntimeImport) = UseState(false);

        var (templateEntries, setTemplateEntries) = UseState<IReadOnlyList<SampleTemplateEntry>>(Array.Empty<SampleTemplateEntry>());
        var (selectedTemplateKey, setSelectedTemplateKey) = UseState(string.Empty);
        var (pendingTemplatePayloadJson, setPendingTemplatePayloadJson) = UseState(string.Empty);
        var (templatePreviewText, setTemplatePreviewText) = UseState(string.Empty);
        var (templateHelpText, setTemplateHelpText) = UseState(string.Empty);

        var (assistantNames, setAssistantNames) = UseState<IReadOnlyList<string>>(new[] { "copilot", "foundry" });
        var (aiWorkspaceTemplateNames, setAiWorkspaceTemplateNames) = UseState<IReadOnlyList<string>>(Array.Empty<string>());
        var (uiTemplateNames, setUiTemplateNames) = UseState<IReadOnlyList<string>>(Array.Empty<string>());
        var (refsTemplateNames, setRefsTemplateNames) = UseState<IReadOnlyList<string>>(Array.Empty<string>());

        var (showAddBoardForm, setShowAddBoardForm) = UseState(false);
        var (addBoardId, setAddBoardId) = UseState(string.Empty);
        var (addBoardLabel, setAddBoardLabel) = UseState(string.Empty);
        var (addBoardPageTitle, setAddBoardPageTitle) = UseState(string.Empty);
        var (addBoardPageSubtitle, setAddBoardPageSubtitle) = UseState(string.Empty);
        var (addBoardAi, setAddBoardAi) = UseState("copilot");
        var (addBoardAiWorkspaceTemplate, setAddBoardAiWorkspaceTemplate) = UseState("default");
        var (addBoardUiTemplate, setAddBoardUiTemplate) = UseState("default");
        var (addBoardRefsTemplate, setAddBoardRefsTemplate) = UseState("localfs-default");
        var (addBoardTemplateKey, setAddBoardTemplateKey) = UseState(string.Empty);

        var (saving, setSaving) = UseState(false);
        var (importing, setImporting) = UseState(false);
        var (exporting, setExporting) = UseState(false);
        var (refreshingWorkspace, setRefreshingWorkspace) = UseState(false);
        var (previewingTemplate, setPreviewingTemplate) = UseState(false);
        var (applyingTemplate, setApplyingTemplate) = UseState(false);
        var (previewingHostConfig, setPreviewingHostConfig) = UseState(false);
        var (addingBoard, setAddingBoard) = UseState(false);

        // Get the embedded board client via hook instead of App.Current
        EmbeddedBoardClient boardClient = UseEmbeddedClient();

        UseEffect(() =>
        {
            string nextRawBoardJson = Props.Config?.RawBoardJson ?? "{}";
            string nextRawUiJson = Props.Config?.RawUiJson ?? "{}";
            string nextRawMetadataJson = Props.Config?.RawMetadataJson ?? "{}";
            string nextRawLayoutJson = string.IsNullOrWhiteSpace(Props.Config?.RawLayoutJson) ? "null" : Props.Config!.RawLayoutJson;

            setRawBoardJson(nextRawBoardJson);
            setRawUiJson(nextRawUiJson);
            setRawMetadataJson(nextRawMetadataJson);
            setRawLayoutJson(nextRawLayoutJson);

            PageDetailsDraft draft = ResolvePageDetailsDraft(Props.BoardId, nextRawBoardJson, nextRawUiJson, nextRawMetadataJson);
            setPageTitle(draft.PageTitle);
            setPageSubtitle(draft.PageSubtitle);
            setRefreshIntervalMinutes(draft.RefreshIntervalMinutes);
            setThemePackId(draft.ThemePackId);
            setUiTemplate(draft.UiTemplate);

            setPageStatus(StatusMessage.Empty);
            setHostStatus(StatusMessage.Empty);
            setImportExportStatus(StatusMessage.Empty);
            setTemplateStatus(StatusMessage.Empty);
            setAddBoardStatus(StatusMessage.Empty);
            setTemplatePreviewText(string.Empty);
            setPendingTemplatePayloadJson(string.Empty);
            setShowHostPreview(false);
            setEffectiveBoardConfigPreviewText(string.Empty);

            setAddBoardId(string.Empty);
            setAddBoardLabel(string.Empty);
            setAddBoardPageTitle(string.Empty);
            setAddBoardPageSubtitle(string.Empty);
            setAddBoardAi("copilot");
            setAddBoardAiWorkspaceTemplate("default");
            setAddBoardUiTemplate("default");
            setAddBoardRefsTemplate("localfs-default");
            setAddBoardTemplateKey(string.Empty);

            _ = LoadCatalogAndTemplatesAsync(
                boardClient,
                setAssistantNames,
                setAiWorkspaceTemplateNames,
                setUiTemplateNames,
                setRefsTemplateNames,
                setHostConfigPathsText,
                setHostConfigPreviewText,
                setTemplateEntries,
                setSelectedTemplateKey,
                setTemplateHelpText,
                setThemePackId,
                setUiTemplate,
                setAddBoardAi,
                setAddBoardAiWorkspaceTemplate,
                setAddBoardUiTemplate,
                setAddBoardRefsTemplate,
                setHostStatus,
                setTemplateStatus,
                draft.ThemePackId,
                draft.UiTemplate);
        }, Props.BoardId, Props.Config?.RawBoardJson ?? string.Empty, Props.Config?.RawUiJson ?? string.Empty, Props.Config?.RawMetadataJson ?? string.Empty, Props.Config?.RawLayoutJson ?? string.Empty);

        ManageBoards manageBoards = UseManageBoards();
        var (activeBoardId, setActiveBoardId) = UseGlobalState<string>(GlobalStateKeys.BoardId, Props.BoardId, WinUiBoardIdStore.SaveOverride);
        var (pendingBoardId, setPendingBoardId) = UseState(activeBoardId);

        var boardOptions = manageBoards.ManagedBoards
            .Select(entry => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = entry.Id,
                ["label"] = string.IsNullOrWhiteSpace(entry.Label) ? entry.Id : entry.Label,
            })
            .ToList();

        void SwitchBoard()
        {
            string target = pendingBoardId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(target) || string.Equals(target, activeBoardId, StringComparison.Ordinal))
            {
                return;
            }

            setActiveBoardId(target);
            App.Current.RequestRestart();
        }

        var sections = new List<Element>();

        sections.Add(
            SectionCard(
                VStack(8,
                    HStack(8,
                        VStack(4,
                            TextBlock("Board Settings").FontSize(20).Bold(),
                            TextBlock("Edit board details from the Reactor shell without falling back to WinUI hosts.")
                                .Opacity(0.72)
                                .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))
                            .Flex(grow: 1),
                        Button("Close", Props.CloseAction).AutomationName("Close board settings").SubtleButton()))));

        sections.Add(
            SectionCard(
                Component<BoardSwitcher, BoardSwitcherProps>(new BoardSwitcherProps(
                    Value: pendingBoardId,
                    Options: boardOptions,
                    CurrentBoardId: activeBoardId,
                    OnChange: setPendingBoardId,
                    OnSwitch: SwitchBoard,
                    SelectDisabled: manageBoards.LoadingManagedBoards,
                    Loading: manageBoards.LoadingManagedBoards))));

        sections.Add(
            SectionCard(
                VStack(10,
                    LabelValue("Board", string.IsNullOrWhiteSpace(Props.BoardId) ? "Board id unavailable." : Props.BoardId),
                    HStack(8,
                        Button(showAddBoardForm ? "Hide new board form" : "New board", () =>
                        {
                            setShowAddBoardForm(!showAddBoardForm);
                            setAddBoardStatus(StatusMessage.Empty);
                        }).AutomationName(showAddBoardForm ? "Hide new board form" : "Open new board form").SubtleButton(),
                        Button("Run tests", Props.OnRunTests ?? (() => { })).AutomationName("Run smoke tests").SubtleButton()))));

        sections.Add(
            SectionCard(
                VStack(10,
                    HStack(8,
                        TextBlock("Page Details").FontSize(18).Bold().Flex(grow: 1),
                        Button(saving ? "Saving..." : "Save", () =>
                        {
                            if (!saving)
                            {
                                _ = SavePageDetailsAsync(
                                    Props.BoardId,
                                    rawBoardJson,
                                    rawUiJson,
                                    rawMetadataJson,
                                    rawLayoutJson,
                                    pageTitle,
                                    pageSubtitle,
                                    refreshIntervalMinutes,
                                    themePackId,
                                    uiTemplate,
                                    showHostPreview,
                                    setSaving,
                                    setPageStatus,
                                    setRawBoardJson,
                                    setRawUiJson,
                                    setRawMetadataJson,
                                    setRawLayoutJson,
                                    setPageTitle,
                                    setPageSubtitle,
                                    setRefreshIntervalMinutes,
                                    setThemePackId,
                                    setUiTemplate,
                                    setEffectiveBoardConfigPreviewText,
                                    setHostStatus);
                            }
                        }).AutomationName("Save page details").AccentButton()),
                    HintText("Edit the page title, subtitle, refresh cadence, theme pack, and UI template without raw JSON editing."),
                    FieldEditor("Page title", pageTitle, setPageTitle),
                    FieldEditor("Page subtitle", pageSubtitle, setPageSubtitle),
                    FieldEditor("Refresh interval (minutes)", refreshIntervalMinutes, setRefreshIntervalMinutes),
                    FieldEditor("Theme pack", themePackId, setThemePackId),
                    HintText($"Available theme packs: {string.Join(", ", BoardTheme.ThemePackIds)}"),
                    FieldEditor("UI template", uiTemplate, setUiTemplate),
                    HintText(BuildOptionsHint("Available UI templates", uiTemplateNames)),
                    StatusBlock(pageStatus))));

        sections.Add(
            SectionCard(
                VStack(10,
                    TextBlock("Host Runtime Config").FontSize(18).Bold(),
                    HintText("Use this only when you need to inspect resolved host wiring."),
                    BuildCodeBlock(hostConfigPathsText, minHeight: 72),
                    HStack(8,
                        Button(previewingHostConfig ? "Refreshing preview..." : "Preview effective board config", () =>
                        {
                            if (!previewingHostConfig)
                            {
                                _ = PreviewEffectiveBoardConfigAsync(
                                    Props.BoardId,
                                    rawBoardJson,
                                    setPreviewingHostConfig,
                                    setShowHostPreview,
                                    setEffectiveBoardConfigPreviewText,
                                    setHostStatus);
                            }
                        }).AutomationName("Preview effective board config").SubtleButton(),
                        Button("Hide preview", () =>
                        {
                            setShowHostPreview(false);
                            setHostStatus(StatusMessage.Empty);
                        }).AutomationName("Hide effective board config preview").SubtleButton()),
                    showHostPreview
                        ? (Element)VStack(8,
                            TextBlock("Resolved host summary").Bold(),
                            BuildCodeBlock(hostConfigPreviewText, minHeight: 140),
                            TextBlock("Resolved effective board config").Bold(),
                            BuildCodeBlock(effectiveBoardConfigPreviewText, minHeight: 220))
                        : HintText("Preview is hidden."),
                    StatusBlock(hostStatus))));

        sections.Add(
            SectionCard(
                VStack(10,
                    TextBlock("Board Import / Export").FontSize(18).Bold(),
                    HintText("Move runtime dumps in and out, or refresh the workspace bootstrap state."),
                    confirmRuntimeImport
                        ? (Element)Component<ChallengeConfirmModal, ChallengeConfirmModalProps>(
                            new ChallengeConfirmModalProps(
                                "This will overwrite the current runtime card state from a local dump file. Cards not present in the file will be removed.",
                                () =>
                                {
                                    if (!importing)
                                    {
                                        setConfirmRuntimeImport(false);
                                        _ = ImportBoardAsync(
                                            Props.BoardId,
                                            setImporting,
                                            setImportExportStatus,
                                            setRawBoardJson,
                                            setRawUiJson,
                                            setRawMetadataJson,
                                            setRawLayoutJson,
                                            setPageTitle,
                                            setPageSubtitle,
                                            setRefreshIntervalMinutes,
                                            setThemePackId,
                                            setUiTemplate,
                                            showHostPreview,
                                            setEffectiveBoardConfigPreviewText,
                                            setHostStatus);
                                    }
                                },
                                () =>
                                {
                                    if (!importing)
                                    {
                                        setConfirmRuntimeImport(false);
                                    }
                                },
                                importing))
                        : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed),
                    HStack(8,
                        Button(importing ? "Importing..." : "Import board", () =>
                        {
                            if (!importing)
                            {
                                setConfirmRuntimeImport(true);
                            }
                        }).AutomationName("Import board dump").SubtleButton(),
                        Button(exporting ? "Exporting..." : "Export board", () =>
                        {
                            if (!exporting)
                            {
                                _ = ExportBoardAsync(Props.BoardId, setExporting, setImportExportStatus);
                            }
                        }).AutomationName("Export board dump").SubtleButton(),
                        Button(refreshingWorkspace ? "Refreshing..." : "Refresh workspace bootstrap", () =>
                        {
                            if (!refreshingWorkspace)
                            {
                                _ = RefreshBoardWorkspaceAsync(
                                    Props.BoardId,
                                    rawBoardJson,
                                    showHostPreview,
                                    setRefreshingWorkspace,
                                    setImportExportStatus,
                                    setEffectiveBoardConfigPreviewText,
                                    setHostStatus);
                            }
                        }).AutomationName("Refresh workspace bootstrap").SubtleButton()),
                    StatusBlock(importExportStatus))));

        sections.Add(
            SectionCard(
                VStack(10,
                    TextBlock("Template Card Ingest").FontSize(18).Bold(),
                    HintText("Preview the card delta first, then ingest into the current board when the result looks right."),
                    FieldEditor("Template key", selectedTemplateKey, setSelectedTemplateKey),
                    HintText(BuildTemplateCatalogHint(templateEntries)),
                    HStack(8,
                        Button(previewingTemplate ? "Preparing preview..." : "Preview ingest", () =>
                        {
                            if (!previewingTemplate)
                            {
                                _ = PreviewTemplateAsync(
                                    Props.BoardId,
                                    selectedTemplateKey,
                                    setPreviewingTemplate,
                                    setTemplateStatus,
                                    setPendingTemplatePayloadJson,
                                    setTemplatePreviewText);
                            }
                        }).AutomationName("Preview template ingest").SubtleButton(),
                        Button(applyingTemplate ? "Ingesting..." : "Ingest template", () =>
                        {
                            if (!applyingTemplate)
                            {
                                _ = ApplyTemplateAsync(
                                    Props.BoardId,
                                    pendingTemplatePayloadJson,
                                    setApplyingTemplate,
                                    setTemplateStatus);
                            }
                        }).AutomationName("Apply template ingest").SubtleButton()),
                    HintText(templateHelpText),
                    StatusBlock(templateStatus),
                    BuildCodeBlock(templatePreviewText, minHeight: 180))));

        if (showAddBoardForm)
        {
            sections.Insert(2,
                SectionCard(
                    VStack(10,
                        HStack(8,
                            TextBlock("Add Board").FontSize(18).Bold().Flex(grow: 1),
                            Button("Cancel", () => setShowAddBoardForm(false)).AutomationName("Cancel add board").SubtleButton(),
                            Button(addingBoard ? "Adding..." : "Add board", () =>
                            {
                                if (!addingBoard)
                                {
                                    _ = AddBoardAsync(
                                        addBoardId,
                                        addBoardLabel,
                                        addBoardPageTitle,
                                        addBoardPageSubtitle,
                                        addBoardAi,
                                        addBoardAiWorkspaceTemplate,
                                        addBoardUiTemplate,
                                        addBoardRefsTemplate,
                                        addBoardTemplateKey,
                                        setAddingBoard,
                                        setAddBoardStatus,
                                        () => setShowAddBoardForm(false));
                                }
                            }).AutomationName("Add board").AccentButton()),
                        HintText("Create a new managed board and optionally seed it from a template."),
                        FieldEditor("Board id", addBoardId, setAddBoardId),
                        FieldEditor("Label", addBoardLabel, setAddBoardLabel),
                        FieldEditor("Page title", addBoardPageTitle, setAddBoardPageTitle),
                        FieldEditor("Page subtitle", addBoardPageSubtitle, setAddBoardPageSubtitle),
                        FieldEditor("AI", addBoardAi, setAddBoardAi),
                        HintText(BuildOptionsHint("Available AI names", assistantNames)),
                        FieldEditor("AI workspace template", addBoardAiWorkspaceTemplate, setAddBoardAiWorkspaceTemplate),
                        HintText(BuildOptionsHint("Available AI workspace templates", aiWorkspaceTemplateNames)),
                        FieldEditor("UI template", addBoardUiTemplate, setAddBoardUiTemplate),
                        HintText(BuildOptionsHint("Available UI templates", uiTemplateNames)),
                        FieldEditor("Refs template", addBoardRefsTemplate, setAddBoardRefsTemplate),
                        HintText(BuildOptionsHint("Available refs templates", refsTemplateNames)),
                        FieldEditor("Optional card template", addBoardTemplateKey, setAddBoardTemplateKey),
                        HintText(templateHelpText),
                        StatusBlock(addBoardStatus))));
        }

        return ScrollViewer(VStack(14, sections.ToArray()))
            .Set(scrollViewer =>
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            });
    }

    private static async Task LoadCatalogAndTemplatesAsync(
        EmbeddedBoardClient boardClient,
        Action<IReadOnlyList<string>> setAssistantNames,
        Action<IReadOnlyList<string>> setAiWorkspaceTemplateNames,
        Action<IReadOnlyList<string>> setUiTemplateNames,
        Action<IReadOnlyList<string>> setRefsTemplateNames,
        Action<string> setHostConfigPathsText,
        Action<string> setHostConfigPreviewText,
        Action<IReadOnlyList<SampleTemplateEntry>> setTemplateEntries,
        Action<string> setSelectedTemplateKey,
        Action<string> setTemplateHelpText,
        Action<string> setThemePackId,
        Action<string> setUiTemplate,
        Action<string> setAddBoardAi,
        Action<string> setAddBoardAiWorkspaceTemplate,
        Action<string> setAddBoardUiTemplate,
        Action<string> setAddBoardRefsTemplate,
        Action<StatusMessage> setHostStatus,
        Action<StatusMessage> setTemplateStatus,
        string currentThemePackId,
        string currentUiTemplate)
    {
        try
        {
            WinUiHostTemplateCatalog hostTemplateCatalog = await boardClient.DescribeHostConfigAsync();
            IReadOnlyList<string> assistantOptions = hostTemplateCatalog.AssistantNames
                .Where(name => string.Equals(name, "copilot", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "foundry", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            setAssistantNames(assistantOptions.Count > 0 ? assistantOptions : hostTemplateCatalog.AssistantNames);
            setAiWorkspaceTemplateNames(hostTemplateCatalog.AiWorkspaceTemplateNames);
            setUiTemplateNames(hostTemplateCatalog.UiTemplateNames);
            setRefsTemplateNames(hostTemplateCatalog.RefsTemplateNames);
            setHostConfigPathsText(
                $"Host config: {hostTemplateCatalog.HostConfigPath}{Environment.NewLine}" +
                $"Templates config: {hostTemplateCatalog.TemplatesConfigPath}{Environment.NewLine}" +
                $"Workspace setup script: {hostTemplateCatalog.SetupSingleAiWorkspaceScriptPath}");
            setHostConfigPreviewText(PrettyJson(hostTemplateCatalog.RawHostSummaryJson));

            setThemePackId(ValueOrFallback(currentThemePackId, BoardTheme.DefaultThemePackId));
            setUiTemplate(ValueOrFallback(currentUiTemplate, "default"));
            setAddBoardAi("copilot");
            setAddBoardAiWorkspaceTemplate("default");
            setAddBoardUiTemplate("default");
            setAddBoardRefsTemplate("localfs-default");
            setHostStatus(StatusMessage.Empty);
        }
        catch (Exception ex)
        {
            setAssistantNames(new[] { "copilot", "foundry" });
            setAiWorkspaceTemplateNames(Array.Empty<string>());
            setUiTemplateNames(Array.Empty<string>());
            setRefsTemplateNames(Array.Empty<string>());
            setHostConfigPathsText(ex.Message);
            setHostConfigPreviewText(string.Empty);
            setHostStatus(StatusMessage.Error(ex.Message));
        }

        try
        {
            IReadOnlyList<SampleTemplateEntry> templates = await boardClient.ListSampleTemplatesAsync();
            setTemplateEntries(templates);
            setSelectedTemplateKey(templates.FirstOrDefault()?.Key ?? string.Empty);
            setTemplateHelpText(templates.Count == 0
                ? "No sample templates are available."
                : "If selected, the template cards will be ingested into the target board.");
            setTemplateStatus(StatusMessage.Empty);
        }
        catch (Exception ex)
        {
            setTemplateEntries(Array.Empty<SampleTemplateEntry>());
            setSelectedTemplateKey(string.Empty);
            setTemplateHelpText(ex.Message);
            setTemplateStatus(StatusMessage.Error(ex.Message));
        }
    }

    private static async Task SavePageDetailsAsync(
        string boardId,
        string rawBoardJson,
        string rawUiJson,
        string rawMetadataJson,
        string rawLayoutJson,
        string pageTitle,
        string pageSubtitle,
        string refreshIntervalMinutes,
        string themePackId,
        string uiTemplate,
        bool refreshPreview,
        Action<bool> setSaving,
        Action<StatusMessage> setPageStatus,
        Action<string> setRawBoardJson,
        Action<string> setRawUiJson,
        Action<string> setRawMetadataJson,
        Action<string> setRawLayoutJson,
        Action<string> setPageTitle,
        Action<string> setPageSubtitle,
        Action<string> setRefreshIntervalMinutes,
        Action<string> setThemePackId,
        Action<string> setUiTemplate,
        Action<string> setEffectiveBoardConfigPreviewText,
        Action<StatusMessage> setHostStatus)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            setPageStatus(StatusMessage.Error("Board id unavailable."));
            return;
        }

        string normalizedTitle = pageTitle.Trim();
        string normalizedSubtitle = pageSubtitle.Trim();
        string normalizedRefresh = refreshIntervalMinutes.Trim();
        string normalizedThemePackId = ValueOrFallback(themePackId, BoardTheme.DefaultThemePackId);
        string normalizedUiTemplate = ValueOrFallback(uiTemplate, "default");

        if (normalizedTitle.Length == 0 || normalizedSubtitle.Length == 0 || normalizedRefresh.Length == 0 || normalizedUiTemplate.Length == 0)
        {
            setPageStatus(StatusMessage.Error("All page detail fields are required."));
            return;
        }

        if (!int.TryParse(normalizedRefresh, out int refreshMinutes) || refreshMinutes <= 0)
        {
            setPageStatus(StatusMessage.Error("Refresh interval must be a positive number of minutes."));
            return;
        }

        setSaving(true);
        setPageStatus(StatusMessage.Info("Saving page details..."));
        try
        {
            App app = App.Current;
            string updatedRawUiJson = MergeThemePackIntoUiJson(rawUiJson, normalizedThemePackId);
            string updatedRawMetadataJson = BuildUpdatedMetadataJson(rawMetadataJson, normalizedTitle, normalizedSubtitle, refreshMinutes);
            string updatedBoardRecordJson = BuildUpdatedBoardRecordJson(rawBoardJson, normalizedUiTemplate);

            ManagedBoardConfigState saved = await app.BoardClient.SaveManagedBoardConfigAsync(
                boardId,
                updatedRawUiJson,
                updatedRawMetadataJson,
                rawLayoutJson,
                updatedBoardRecordJson);

            setRawBoardJson(saved.RawBoardJson);
            setRawUiJson(saved.RawUiJson);
            setRawMetadataJson(saved.RawMetadataJson);
            setRawLayoutJson(saved.RawLayoutJson);
            app.BoardStore.SetManagedBoardConfig(saved);

            PageDetailsDraft draft = ResolvePageDetailsDraft(boardId, saved.RawBoardJson, saved.RawUiJson, saved.RawMetadataJson);
            setPageTitle(draft.PageTitle);
            setPageSubtitle(draft.PageSubtitle);
            setRefreshIntervalMinutes(draft.RefreshIntervalMinutes);
            setThemePackId(draft.ThemePackId);
            setUiTemplate(draft.UiTemplate);

            if (refreshPreview)
            {
                setEffectiveBoardConfigPreviewText(await app.BoardClient.ResolveEffectiveBoardConfigAsync(boardId, saved.RawBoardJson));
            }

            setHostStatus(StatusMessage.Empty);
            setPageStatus(StatusMessage.Success("Saved."));
        }
        catch (Exception ex)
        {
            setPageStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setSaving(false);
        }
    }

    private static async Task PreviewEffectiveBoardConfigAsync(
        string boardId,
        string rawBoardJson,
        Action<bool> setPreviewingHostConfig,
        Action<bool> setShowHostPreview,
        Action<string> setEffectiveBoardConfigPreviewText,
        Action<StatusMessage> setHostStatus)
    {
        setPreviewingHostConfig(true);
        try
        {
            setShowHostPreview(true);
            setEffectiveBoardConfigPreviewText(string.IsNullOrWhiteSpace(boardId)
                ? string.Empty
                : await App.Current.BoardClient.ResolveEffectiveBoardConfigAsync(boardId, rawBoardJson));
            setHostStatus(StatusMessage.Success("Effective board config preview updated."));
        }
        catch (Exception ex)
        {
            setHostStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setPreviewingHostConfig(false);
        }
    }

    private static async Task ImportBoardAsync(
        string boardId,
        Action<bool> setImporting,
        Action<StatusMessage> setImportExportStatus,
        Action<string> setRawBoardJson,
        Action<string> setRawUiJson,
        Action<string> setRawMetadataJson,
        Action<string> setRawLayoutJson,
        Action<string> setPageTitle,
        Action<string> setPageSubtitle,
        Action<string> setRefreshIntervalMinutes,
        Action<string> setThemePackId,
        Action<string> setUiTemplate,
        bool refreshPreview,
        Action<string> setEffectiveBoardConfigPreviewText,
        Action<StatusMessage> setHostStatus)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            setImportExportStatus(StatusMessage.Error("Board id unavailable."));
            return;
        }

        setImporting(true);
        setImportExportStatus(StatusMessage.Info("Importing board dump..."));
        try
        {
            string? json = await NativeFilePicker.PickJsonTextAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                setImportExportStatus(StatusMessage.Info("Import cancelled."));
                return;
            }

            App app = App.Current;
            await app.BoardClient.ApplyImportBoardAsync(boardId, json, "replace", applyBoardMetadata: true);
            ManagedBoardConfigState? saved = await app.BoardClient.GetManagedBoardConfigAsync(boardId);
            if (saved is not null)
            {
                setRawBoardJson(saved.RawBoardJson);
                setRawUiJson(saved.RawUiJson);
                setRawMetadataJson(saved.RawMetadataJson);
                setRawLayoutJson(saved.RawLayoutJson);
                app.BoardStore.SetManagedBoardConfig(saved);

                PageDetailsDraft draft = ResolvePageDetailsDraft(boardId, saved.RawBoardJson, saved.RawUiJson, saved.RawMetadataJson);
                setPageTitle(draft.PageTitle);
                setPageSubtitle(draft.PageSubtitle);
                setRefreshIntervalMinutes(draft.RefreshIntervalMinutes);
                setThemePackId(draft.ThemePackId);
                setUiTemplate(draft.UiTemplate);

                if (refreshPreview)
                {
                    setEffectiveBoardConfigPreviewText(await app.BoardClient.ResolveEffectiveBoardConfigAsync(boardId, saved.RawBoardJson));
                }
            }

            await app.BoardClient.RefreshBoardAsync();
            setHostStatus(StatusMessage.Empty);
            setImportExportStatus(StatusMessage.Success("Board import applied."));
        }
        catch (Exception ex)
        {
            setImportExportStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setImporting(false);
        }
    }

    private static async Task ExportBoardAsync(string boardId, Action<bool> setExporting, Action<StatusMessage> setImportExportStatus)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            setImportExportStatus(StatusMessage.Error("Board id unavailable."));
            return;
        }

        setExporting(true);
        setImportExportStatus(StatusMessage.Info("Exporting board dump..."));
        try
        {
            string json = await App.Current.BoardClient.ExportBoardAsync(boardId);
            bool saved = await NativeFilePicker.SaveJsonTextAsync($"{boardId}-runtime-dump.json", json);
            setImportExportStatus(saved
                ? StatusMessage.Success("Board export saved.")
                : StatusMessage.Info("Export cancelled."));
        }
        catch (Exception ex)
        {
            setImportExportStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setExporting(false);
        }
    }

    private static async Task RefreshBoardWorkspaceAsync(
        string boardId,
        string rawBoardJson,
        bool refreshPreview,
        Action<bool> setRefreshingWorkspace,
        Action<StatusMessage> setImportExportStatus,
        Action<string> setEffectiveBoardConfigPreviewText,
        Action<StatusMessage> setHostStatus)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            setImportExportStatus(StatusMessage.Error("Board id unavailable."));
            return;
        }

        setRefreshingWorkspace(true);
        setImportExportStatus(StatusMessage.Info("Refreshing workspace bootstrap..."));
        try
        {
            App app = App.Current;
            await app.BoardClient.SetupBoardWorkspaceAsync(boardId);
            await app.BoardClient.RefreshManagedBoardAsync(boardId);
            await app.BoardClient.RefreshBoardAsync();
            if (refreshPreview)
            {
                setEffectiveBoardConfigPreviewText(await app.BoardClient.ResolveEffectiveBoardConfigAsync(boardId, rawBoardJson));
            }

            setHostStatus(StatusMessage.Empty);
            setImportExportStatus(StatusMessage.Success("Workspace bootstrap refreshed."));
        }
        catch (Exception ex)
        {
            setImportExportStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setRefreshingWorkspace(false);
        }
    }

    private static async Task PreviewTemplateAsync(
        string boardId,
        string templateKey,
        Action<bool> setPreviewingTemplate,
        Action<StatusMessage> setTemplateStatus,
        Action<string> setPendingTemplatePayloadJson,
        Action<string> setTemplatePreviewText)
    {
        if (string.IsNullOrWhiteSpace(boardId) || string.IsNullOrWhiteSpace(templateKey))
        {
            setTemplateStatus(StatusMessage.Error("Select a template to preview."));
            return;
        }

        setPreviewingTemplate(true);
        setTemplateStatus(StatusMessage.Info("Preparing template preview..."));
        try
        {
            App app = App.Current;
            SampleTemplateEnvelope template = await app.BoardClient.GetSampleTemplateAsync(templateKey);
            BoardImportPreview preview = await app.BoardClient.PreviewImportBoardAsync(boardId, template.RawPayloadJson, "ingest");
            setPendingTemplatePayloadJson(template.RawPayloadJson);
            setTemplatePreviewText(BuildTemplatePreviewText(template, preview));
            setTemplateStatus(StatusMessage.Success("Template preview ready."));
        }
        catch (Exception ex)
        {
            setPendingTemplatePayloadJson(string.Empty);
            setTemplatePreviewText(string.Empty);
            setTemplateStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setPreviewingTemplate(false);
        }
    }

    private static async Task ApplyTemplateAsync(
        string boardId,
        string pendingTemplatePayloadJson,
        Action<bool> setApplyingTemplate,
        Action<StatusMessage> setTemplateStatus)
    {
        if (string.IsNullOrWhiteSpace(boardId) || string.IsNullOrWhiteSpace(pendingTemplatePayloadJson))
        {
            setTemplateStatus(StatusMessage.Error("Preview a template before ingesting it."));
            return;
        }

        setApplyingTemplate(true);
        setTemplateStatus(StatusMessage.Info("Ingesting template cards..."));
        try
        {
            App app = App.Current;
            await app.BoardClient.ApplyImportBoardAsync(boardId, pendingTemplatePayloadJson, "ingest", applyBoardMetadata: false);
            await app.BoardClient.RefreshBoardAsync();
            setTemplateStatus(StatusMessage.Success("Template cards ingested."));
        }
        catch (Exception ex)
        {
            setTemplateStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setApplyingTemplate(false);
        }
    }

    private static async Task AddBoardAsync(
        string boardId,
        string label,
        string pageTitle,
        string pageSubtitle,
        string ai,
        string aiWorkspaceTemplate,
        string uiTemplate,
        string refsTemplate,
        string templateKey,
        Action<bool> setAddingBoard,
        Action<StatusMessage> setAddBoardStatus,
        Action onSuccess)
    {
        setAddingBoard(true);
        setAddBoardStatus(StatusMessage.Info("Adding board..."));
        try
        {
            App app = App.Current;
            var request = new ManagedBoardCreateRequest(
                boardId,
                label,
                pageTitle,
                pageSubtitle,
                ValueOrFallback(ai, "copilot"),
                ValueOrFallback(aiWorkspaceTemplate, "default"),
                ValueOrFallback(uiTemplate, "default"),
                ValueOrFallback(refsTemplate, "localfs-default"),
                templateKey.Trim());

            ManagedBoardListEntry created = await app.BoardClient.AddManagedBoardAsync(request);
            if (!string.IsNullOrWhiteSpace(request.TemplateKey))
            {
                SampleTemplateEnvelope template = await app.BoardClient.GetSampleTemplateAsync(request.TemplateKey);
                await app.BoardClient.ApplyImportBoardAsync(created.Id, template.RawPayloadJson, "ingest", applyBoardMetadata: false);
            }

            await app.BoardClient.SetupBoardWorkspaceAsync(created.Id);
            setAddBoardStatus(StatusMessage.Success("Board created."));
            onSuccess();
        }
        catch (Exception ex)
        {
            setAddBoardStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setAddingBoard(false);
        }
    }

    private static Element FieldEditor(string label, string value, Action<string> setValue)
    {
        return VStack(4,
            TextBlock(label).Bold().Opacity(0.82),
            TextBox(value, setValue)
                .AutomationName(label)
                .Set(textBox => textBox.TextWrapping = TextWrapping.Wrap));
    }

    private static Element LabelValue(string label, string value)
    {
        return VStack(4,
            TextBlock(label).Bold().Opacity(0.82),
            TextBlock(value).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords));
    }

    private static Element HintText(string message)
    {
        return TextBlock(message)
            .Opacity(0.68)
            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static Element StatusBlock(StatusMessage status)
    {
        return string.IsNullOrWhiteSpace(status.Message)
            ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
            : TextBlock(status.Message)
                .Foreground(CreateStatusBrush(status.Kind))
                .Opacity(0.88)
                .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static Element BuildCodeBlock(string value, double minHeight)
    {
        return TextBox(value)
            .AutomationName("Read only details")
            .IsReadOnly(true)
            .AcceptsReturn(true)
            .TextWrapping(TextWrapping.Wrap)
            .Set(textBox =>
            {
                textBox.MinHeight = minHeight;
                textBox.FontFamily = new FontFamily("Consolas");
            });
    }

    private static Element SectionCard(Element content)
    {
        return Border(content)
            .Padding(14)
            .Background(BoardTheme.ResolveBrush("CardBackgroundFillColorDefaultBrush", Colors.White))
            .WithBorder(BoardTheme.ResolveBrush("BoardBorderStrongBrush", Colors.LightGray), 1)
            .CornerRadius(14);
    }

    private static Brush CreateStatusBrush(StatusKind kind)
    {
        return kind switch
        {
            StatusKind.Error => new SolidColorBrush(Colors.IndianRed),
            StatusKind.Success => new SolidColorBrush(Colors.SeaGreen),
            _ => BoardTheme.ResolveBrush("BoardTextMutedBrush", Colors.DimGray),
        };
    }

    private static string BuildOptionsHint(string label, IReadOnlyList<string> options)
    {
        return options.Count == 0
            ? $"{label}: none exposed by the host config."
            : $"{label}: {string.Join(", ", options)}";
    }

    private static string BuildTemplateCatalogHint(IReadOnlyList<SampleTemplateEntry> templates)
    {
        if (templates.Count == 0)
        {
            return "No sample templates are available.";
        }

        return string.Join(Environment.NewLine, templates.Select(template =>
            string.IsNullOrWhiteSpace(template.Description)
                ? $"{template.Key}: {template.Label}"
                : $"{template.Key}: {template.Label} - {template.Description}"));
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
        string refreshMinutes = ResolveRefreshIntervalMinutes(rawMetadataJson);
        string theme = BoardTheme.ResolveThemePackIdFromUiJson(rawUiJson);
        string template = ResolveUiTemplate(rawBoardJson);
        return new PageDetailsDraft(title, subtitle, refreshMinutes, theme, template);
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

    private enum StatusKind
    {
        Neutral,
        Success,
        Error
    }

    private sealed record StatusMessage(string Message, StatusKind Kind)
    {
        public static readonly StatusMessage Empty = new(string.Empty, StatusKind.Neutral);

        public static StatusMessage Info(string message) => new(message, StatusKind.Neutral);

        public static StatusMessage Success(string message) => new(message, StatusKind.Success);

        public static StatusMessage Error(string message) => new(message, StatusKind.Error);
    }
}