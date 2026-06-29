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

public sealed record BoardConfigPaneProps(
    string BoardId,
    Action CloseAction,
    EmbeddedBoardClient BoardClient,
    ManageBoards ManageBoards,
    string ActiveBoardId,
    Action<string> SetActiveBoardId,
    string ActiveServerUrl,
    Action<string> SetActiveServerUrl,
    string InitialServerUrl,
    string LiveRuntimeServerUrl,
    Action<ManagedBoardConfigState?> SetManagedBoardConfig,
    Action? OnRunSmokeRunner = null);

public sealed class BoardConfigPane : HookComponent<BoardConfigPaneProps>
{
    private sealed record TemplatePreviewState(
        string TemplateLabel,
        IReadOnlyList<object?> CardsToReplace,
        IReadOnlyList<object?> CardsToAdd,
        IReadOnlyList<object?> InvalidCards,
        string PayloadJson);

    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };
    private const int DefaultRefreshAllIntervalSeconds = 30 * 60;

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        BoardConfig? boardConfig = UseBoardConfig(Props.BoardId);
        BoardVisuals visualsHook = UseBoardVisuals(Props.BoardId);

        string rawBoardJson = boardConfig?.Board?.ToJsonString(PrettyJsonOptions) ?? "{}";
        string rawUiJson = visualsHook.Visuals.Ui.ToJsonString(PrettyJsonOptions);
        string rawMetadataJson = boardConfig?.Metadata.ToJsonString(PrettyJsonOptions) ?? "{}";
        string rawLayoutJson = visualsHook.Visuals.LayoutBlob.ToJsonString(PrettyJsonOptions);
        PageDetailsState pageDetails = PageDetailsState.FromRaw(Props.BoardId, rawBoardJson, rawMetadataJson);

        Element SectionCard(Element content) => Border(content)
            .Padding(14)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorderStrong, 1)
            .CornerRadius(14);

        Brush CreateStatusBrush(StatusKind kind) => kind switch
        {
            StatusKind.Error => theme.StatusError,
            StatusKind.Success => theme.StatusSuccess,
            _ => theme.TextMuted,
        };

        Element StatusBlock(StatusMessage status) => string.IsNullOrWhiteSpace(status.Message)
            ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
            : TextBlock(status.Message)
                .Foreground(CreateStatusBrush(status.Kind))
                .Opacity(0.88)
                .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);

        var (pageStatus, setPageStatus) = UseState(StatusMessage.Empty);
        var (importExportStatus, setImportExportStatus) = UseState(StatusMessage.Empty);
        var (templateStatus, setTemplateStatus) = UseState(StatusMessage.Empty);
        var (addBoardStatus, setAddBoardStatus) = UseState(StatusMessage.Empty);

        var (confirmRuntimeImport, setConfirmRuntimeImport) = UseState(false);

        var (templateEntries, setTemplateEntries) = UseState<IReadOnlyList<SampleTemplateEntry>>(Array.Empty<SampleTemplateEntry>());
        var (selectedTemplateKey, setSelectedTemplateKey) = UseState(string.Empty);
        var (templatePreview, setTemplatePreview) = UseState<TemplatePreviewState?>(null);
        var (templateHelpText, setTemplateHelpText) = UseState(string.Empty);

        var (showAddBoardForm, setShowAddBoardForm) = UseState(false);

        var (saving, setSaving) = UseState(false);
        var (importing, setImporting) = UseState(false);
        var (exporting, setExporting) = UseState(false);
        var (refreshingWorkspace, setRefreshingWorkspace) = UseState(false);
        var (previewingTemplate, setPreviewingTemplate) = UseState(false);
        var (applyingTemplate, setApplyingTemplate) = UseState(false);
        var (addingBoard, setAddingBoard) = UseState(false);

        IReadOnlyList<string> uiTemplateNames = BuildUiTemplateOptions(pageDetails.UiTemplate);
        bool smokeRunnerEnabled = string.Equals(Props.BoardId, "live-test-frontend", StringComparison.Ordinal);
        string smokeRunnerTitle = smokeRunnerEnabled
            ? "Run the in-app smoke suite against the live-test-frontend board"
            : "Smoke suite is only available when the active board id is live-test-frontend";

        UseEffect(() =>
        {
            setPageStatus(StatusMessage.Empty);
            setImportExportStatus(StatusMessage.Empty);
            setTemplateStatus(StatusMessage.Empty);
            setAddBoardStatus(StatusMessage.Empty);
            setTemplatePreview(null);

            _ = LoadCatalogAndTemplatesAsync(
                Props.BoardClient,
                setTemplateEntries,
                setSelectedTemplateKey,
                setTemplateHelpText,
                setTemplateStatus);
            }, Props.BoardId, rawBoardJson, rawMetadataJson, rawLayoutJson);

            ManageBoards manageBoards = Props.ManageBoards;
            var (pendingBoardId, setPendingBoardId) = UseState(Props.ActiveBoardId);

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
            if (string.IsNullOrEmpty(target) || string.Equals(target, Props.ActiveBoardId, StringComparison.Ordinal))
            {
                return;
            }

            Props.SetActiveBoardId(target);
        }

        IReadOnlyList<object?> templateEntryOptions = templateEntries
            .Select(entry => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = entry.Key,
                ["label"] = string.IsNullOrWhiteSpace(entry.Label) ? entry.Key : entry.Label,
            })
            .ToList();

        void CloseAddBoardPane()
        {
            if (!addingBoard)
            {
                setShowAddBoardForm(false);
                setAddBoardStatus(StatusMessage.Empty);
            }
        }

        Action<IReadOnlyDictionary<string, object?>> submitAddBoard = values =>
        {
            if (addingBoard)
            {
                return;
            }

            _ = AddBoardAsync(
                Props.BoardClient,
                values,
                setAddingBoard,
                setAddBoardStatus,
                () =>
                {
                    setShowAddBoardForm(false);
                    setAddBoardStatus(StatusMessage.Success("Board created."));
                });
        };

        void CloseTemplatePreviewPane()
        {
            if (!applyingTemplate)
            {
                setTemplatePreview(null);
                setTemplateStatus(StatusMessage.Empty);
            }
        }

        var sections = new List<Element>();

        sections.Add(
            SectionCard(
                Component<BoardSwitcher, BoardSwitcherProps>(new BoardSwitcherProps(
                    Value: pendingBoardId,
                    Options: boardOptions,
                    CurrentBoardId: Props.ActiveBoardId,
                    OnChange: setPendingBoardId,
                    OnSwitch: SwitchBoard,
                    SelectDisabled: manageBoards.LoadingManagedBoards,
                    Loading: manageBoards.LoadingManagedBoards,
                    SmokeRunnerEnabled: smokeRunnerEnabled,
                    OnRunSmokeRunner: Props.OnRunSmokeRunner,
                    SmokeRunnerTitle: smokeRunnerTitle))
                        .WithKey($"{Props.ActiveBoardId}|{Props.ActiveServerUrl}")));

        sections.Add(
            SectionCard(
                VStack(10,
                    HStack(8,
                        Button("New board", () =>
                        {
                            setShowAddBoardForm(true);
                            setAddBoardStatus(StatusMessage.Empty);
                        }).AutomationName("Open new board form").SubtleButton()))));

        sections.Add(
            SectionCard(
                Component<EditPageDetails, EditPageDetailsProps>(new EditPageDetailsProps(
                    PageTitle: pageDetails.PageTitle,
                    PageSubtitle: pageDetails.PageSubtitle,
                    RefreshIntervalMinutes: pageDetails.RefreshIntervalMinutes,
                    CurrentUiTemplate: pageDetails.UiTemplate,
                    UiTemplateOptions: uiTemplateNames,
                    Saving: saving,
                    ErrorMessage: pageStatus.Kind == StatusKind.Error ? pageStatus.Message : string.Empty,
                    SuccessMessage: pageStatus.Kind == StatusKind.Success ? pageStatus.Message : string.Empty,
                    OnSave: values =>
                    {
                        if (!saving)
                        {
                            _ = SavePageDetailsAsync(
                                Props.BoardClient,
                                Props.BoardId,
                                rawBoardJson,
                                rawUiJson,
                                rawMetadataJson,
                                rawLayoutJson,
                                ReadString(values, "pageTitle"),
                                ReadString(values, "pageSubtitle"),
                                ReadString(values, "refreshAllIntervalMinutes"),
                                ReadString(values, "uiTemplate"),
                                setSaving,
                                setPageStatus,
                                Props.SetManagedBoardConfig);
                        }
                    }))));

        sections.Add(
            SectionCard(
                VStack(10,
                    Component<ServerSwitcher, ServerSwitcherProps>(
                        new ServerSwitcherProps(Props.ActiveServerUrl, Props.SetActiveServerUrl, Props.LiveRuntimeServerUrl)),
                    string.IsNullOrWhiteSpace(manageBoards.ManageBoardsError)
                        ? (Element)TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                        : TextBlock($"Board list error: {manageBoards.ManageBoardsError}")
                            .Opacity(0.78)
                            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))));

        sections.Add(
            SectionCard(
                VStack(10,
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
                                            Props.BoardClient,
                                            Props.BoardId,
                                            setImporting,
                                            setImportExportStatus,
                                            Props.SetManagedBoardConfig);
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
                    Component<BoardImportExport, BoardImportExportProps>(new BoardImportExportProps(
                        OnImport: () =>
                        {
                            if (!importing)
                            {
                                setConfirmRuntimeImport(true);
                            }
                        },
                        OnExport: () =>
                        {
                            if (!exporting)
                            {
                                _ = ExportBoardAsync(Props.BoardClient, Props.BoardId, setExporting, setImportExportStatus);
                            }
                        },
                        OnRefreshBootstrap: () =>
                        {
                            if (!refreshingWorkspace)
                            {
                                _ = RefreshBoardWorkspaceAsync(
                                    Props.BoardClient,
                                    Props.BoardId,
                                    setRefreshingWorkspace,
                                    setImportExportStatus);
                            }
                        },
                        Importing: importing,
                        Exporting: exporting,
                        Refreshing: refreshingWorkspace,
                        Disabled: string.IsNullOrWhiteSpace(Props.BoardId))),
                    StatusBlock(importExportStatus))));

        sections.Add(
            SectionCard(
                VStack(10,
                    Component<TemplateCardIngest, TemplateCardIngestProps>(new TemplateCardIngestProps(
                        Entries: templateEntryOptions,
                        SelectedKey: selectedTemplateKey,
                        OnSelect: setSelectedTemplateKey,
                        OnIngest: () =>
                        {
                            if (!previewingTemplate)
                            {
                                _ = PreviewTemplateAsync(
                                    Props.BoardClient,
                                    Props.BoardId,
                                    selectedTemplateKey,
                                    setPreviewingTemplate,
                                    setTemplateStatus,
                                    setTemplatePreview);
                            }
                        },
                        Loading: false,
                        Ingesting: applyingTemplate,
                        Preparing: previewingTemplate,
                        ErrorMessage: templateHelpText,
                        Disabled: string.IsNullOrWhiteSpace(Props.BoardId))),
                    HintText(templateHelpText),
                    StatusBlock(templateStatus),
                    HintText(BuildTemplateCatalogHint(templateEntries)))));

        if (showAddBoardForm)
        {
            return ScrollViewer(
                Component<ConfigSubPane, ConfigSubPaneProps>(new ConfigSubPaneProps(
                    Title: "Add board",
                    OnBack: CloseAddBoardPane,
                    Children:
                    [
                        HintText("Create a new managed board and optionally seed it from a template."),
                        Component<AddBoard, AddBoardProps>(new AddBoardProps(
                            OnClose: CloseAddBoardPane,
                            OnSubmit: values =>
                            {
                                submitAddBoard(values);
                                return Task.CompletedTask;
                            },
                            TemplateOptions: templateEntryOptions,
                            LoadingTemplates: false,
                            Submitting: addingBoard,
                            ErrorMessage: addBoardStatus.Kind == StatusKind.Error ? addBoardStatus.Message : string.Empty)),
                        HintText(templateHelpText),
                        addBoardStatus.Kind == StatusKind.Success
                            ? StatusBlock(addBoardStatus)
                            : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                    ])))
                .Set(scrollViewer =>
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                });
        }

        if (templatePreview is not null)
        {
            return ScrollViewer(
                Component<ConfigSubPane, ConfigSubPaneProps>(new ConfigSubPaneProps(
                    Title: "Ingest Cards from Template",
                    OnBack: CloseTemplatePreviewPane,
                    Children:
                    [
                        Component<TemplateIngestPreview, TemplateIngestPreviewProps>(new TemplateIngestPreviewProps(
                            TemplateLabel: templatePreview.TemplateLabel,
                            CardsToReplace: templatePreview.CardsToReplace,
                            CardsToAdd: templatePreview.CardsToAdd,
                            InvalidCards: templatePreview.InvalidCards,
                            Ingesting: applyingTemplate,
                            OnCancel: CloseTemplatePreviewPane,
                            OnConfirm: () =>
                            {
                                if (!applyingTemplate)
                                {
                                    _ = ApplyTemplateAsync(
                                        Props.BoardClient,
                                        Props.BoardId,
                                        templatePreview.PayloadJson,
                                        setApplyingTemplate,
                                        setTemplateStatus,
                                        () => setTemplatePreview(null));
                                }
                            }))
                    ])))
                .Set(scrollViewer =>
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                });
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
        Action<IReadOnlyList<SampleTemplateEntry>> setTemplateEntries,
        Action<string> setSelectedTemplateKey,
        Action<string> setTemplateHelpText,
        Action<StatusMessage> setTemplateStatus)
    {
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
        EmbeddedBoardClient boardClient,
        string boardId,
        string rawBoardJson,
        string rawUiJson,
        string rawMetadataJson,
        string rawLayoutJson,
        string pageTitle,
        string pageSubtitle,
        string refreshIntervalMinutes,
        string uiTemplate,
        Action<bool> setSaving,
        Action<StatusMessage> setPageStatus,
        Action<ManagedBoardConfigState?> setManagedBoardConfig)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            setPageStatus(StatusMessage.Error("Board id unavailable."));
            return;
        }

        string normalizedTitle = pageTitle.Trim();
        string normalizedSubtitle = pageSubtitle.Trim();
        string normalizedRefresh = refreshIntervalMinutes.Trim();
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
            PageDetailsState nextDetails = new(
                normalizedTitle,
                normalizedSubtitle,
                Math.Max(1, refreshMinutes).ToString(),
                normalizedUiTemplate);
            string updatedRawMetadataJson = nextDetails.ApplyMetadata(rawMetadataJson);
            string updatedBoardRecordJson = nextDetails.ApplyBoard(rawBoardJson);

            ManagedBoardConfigState saved = await boardClient.SaveManagedBoardConfigAsync(
                boardId,
                rawUiJson,
                updatedRawMetadataJson,
                rawLayoutJson,
                updatedBoardRecordJson);

            setManagedBoardConfig(saved);
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

    private static async Task SaveThemeAsync(
        EmbeddedBoardClient boardClient,
        string boardId,
        Func<string, JsonNode?, Task<JsonObject?>> shallowMerge,
        string themePackId,
        Action<bool> setSavingTheme,
        Action<StatusMessage> setThemeStatus,
        Action<ManagedBoardConfigState?> setManagedBoardConfig)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            setThemeStatus(StatusMessage.Error("Board id unavailable."));
            return;
        }

        setSavingTheme(true);
        setThemeStatus(StatusMessage.Info("Saving theme..."));
        try
        {
            string normalizedThemePackId = BoardTheme.NormalizeThemePackId(themePackId);
            await shallowMerge("theme", JsonValue.Create(normalizedThemePackId));
            ManagedBoardConfigState? saved = await boardClient.GetManagedBoardConfigAsync(boardId);
            if (saved is not null)
            {
                setManagedBoardConfig(saved);
            }

            setThemeStatus(StatusMessage.Success("Saved."));
        }
        catch (Exception ex)
        {
            setThemeStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setSavingTheme(false);
        }
    }

    private static async Task ImportBoardAsync(
        EmbeddedBoardClient boardClient,
        string boardId,
        Action<bool> setImporting,
        Action<StatusMessage> setImportExportStatus,
        Action<ManagedBoardConfigState?> setManagedBoardConfig)
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

            await boardClient.ApplyImportBoardAsync(boardId, json, "replace", applyBoardMetadata: true);
            ManagedBoardConfigState? saved = await boardClient.GetManagedBoardConfigAsync(boardId);
            if (saved is not null)
            {
                setManagedBoardConfig(saved);
            }

            await boardClient.RefreshBoardAsync();
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

    private static async Task ExportBoardAsync(EmbeddedBoardClient boardClient, string boardId, Action<bool> setExporting, Action<StatusMessage> setImportExportStatus)
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
            string json = await boardClient.ExportBoardAsync(boardId);
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
        EmbeddedBoardClient boardClient,
        string boardId,
        Action<bool> setRefreshingWorkspace,
        Action<StatusMessage> setImportExportStatus)
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
            await boardClient.RefreshManagedBoardAsync(boardId);
            await boardClient.RefreshBoardAsync();
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
        EmbeddedBoardClient boardClient,
        string boardId,
        string templateKey,
        Action<bool> setPreviewingTemplate,
        Action<StatusMessage> setTemplateStatus,
        Action<TemplatePreviewState?> setTemplatePreview)
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
            SampleTemplateEnvelope template = await boardClient.GetSampleTemplateAsync(templateKey);
            BoardImportPreview preview = await boardClient.PreviewImportBoardAsync(boardId, template.RawPayloadJson, "ingest");
            setTemplatePreview(new TemplatePreviewState(
                string.IsNullOrWhiteSpace(template.Label) ? template.Key : template.Label,
                preview.ReplaceIds.Select(id => (object?)new Dictionary<string, object?> { ["id"] = id, ["title"] = string.Empty }).ToList(),
                preview.AddIds.Select(id => (object?)new Dictionary<string, object?> { ["id"] = id, ["title"] = string.Empty }).ToList(),
                preview.InvalidCards.Select(card => (object?)new Dictionary<string, object?>
                {
                    ["id"] = card.Id,
                    ["title"] = card.Title,
                    ["issues"] = card.Issues.ToArray()
                }).ToList(),
                template.RawPayloadJson));
            setTemplateStatus(StatusMessage.Success("Template preview ready."));
        }
        catch (Exception ex)
        {
            setTemplatePreview(null);
            setTemplateStatus(StatusMessage.Error(ex.Message));
        }
        finally
        {
            setPreviewingTemplate(false);
        }
    }

    private static async Task ApplyTemplateAsync(
        EmbeddedBoardClient boardClient,
        string boardId,
        string pendingTemplatePayloadJson,
        Action<bool> setApplyingTemplate,
        Action<StatusMessage> setTemplateStatus,
        Action? onSuccess = null)
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
            await boardClient.ApplyImportBoardAsync(boardId, pendingTemplatePayloadJson, "ingest", applyBoardMetadata: false);
            await boardClient.RefreshBoardAsync();
            onSuccess?.Invoke();
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
        EmbeddedBoardClient boardClient,
        IReadOnlyDictionary<string, object?> values,
        Action<bool> setAddingBoard,
        Action<StatusMessage> setAddBoardStatus,
        Action onSuccess)
    {
        setAddingBoard(true);
        setAddBoardStatus(StatusMessage.Info("Adding board..."));
        try
        {
            string boardId = ReadString(values, "boardId");
            string label = ReadString(values, "label");
            string pageTitle = ReadString(values, "pageTitle");
            string pageSubtitle = ReadString(values, "pageSubtitle");
            string ai = ReadString(values, "ai");
            string aiWorkspaceTemplate = ReadString(values, "aiWorkspaceTemplate");
            string uiTemplate = ReadString(values, "uiTemplate");
            string refsTemplate = ReadString(values, "refsTemplate");
            string templateKey = ReadString(values, "templateKey");

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

            ManagedBoardListEntry created = await boardClient.AddManagedBoardAsync(request);
            if (!string.IsNullOrWhiteSpace(request.TemplateKey))
            {
                SampleTemplateEnvelope template = await boardClient.GetSampleTemplateAsync(request.TemplateKey);
                await boardClient.ApplyImportBoardAsync(created.Id, template.RawPayloadJson, "ingest", applyBoardMetadata: false);
            }

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

    private static string ReadString(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out object? value)
            ? value?.ToString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static IReadOnlyList<string> BuildUiTemplateOptions(string currentUiTemplate)
    {
        string normalized = ValueOrFallback(currentUiTemplate, "default");
        return new[] { normalized, "default" }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
            JsonNode? node = JsonNode.Parse(raw);
            return node?.ToJsonString(PrettyJsonOptions) ?? raw;
        }
        catch
        {
            return raw;
        }
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

    private static string ValueOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed record PageDetailsState(
        string PageTitle,
        string PageSubtitle,
        string RefreshIntervalMinutes,
        string UiTemplate)
    {
        public static PageDetailsState FromRaw(string boardId, string rawBoardJson, string rawMetadataJson)
        {
            JsonObject metadata = ParseObject(rawMetadataJson);
            JsonObject board = ParseObject(rawBoardJson);

            string fallbackTitle = string.IsNullOrWhiteSpace(boardId) ? "Demo Boards" : boardId;
            string title = ReadString(metadata, "pageTitle", fallbackTitle);
            string subtitle = ReadString(metadata, "pageSubtitle", "Embedded board workspace");
            string refreshMinutes = ReadRefreshMinutes(metadata);
            string uiTemplate = ReadString(board, "uiTemplate", "default");

            return new PageDetailsState(title, subtitle, refreshMinutes, uiTemplate);
        }

        public string ApplyMetadata(string currentRawMetadataJson)
        {
            JsonObject metadata = ParseObject(currentRawMetadataJson);
            metadata["pageTitle"] = PageTitle;
            metadata["pageSubtitle"] = PageSubtitle;
            metadata["refreshAllIntervalSeconds"] = Math.Max(1, ParsePositiveIntOrDefault(RefreshIntervalMinutes, DefaultRefreshAllIntervalSeconds / 60)) * 60;
            return metadata.ToJsonString(PrettyJsonOptions);
        }

        public string ApplyBoard(string currentRawBoardJson)
        {
            JsonObject board = ParseObject(currentRawBoardJson);
            board["uiTemplate"] = UiTemplate;
            return board.ToJsonString(PrettyJsonOptions);
        }

        private static string ReadString(JsonObject source, string propertyName, string fallback)
        {
            return source[propertyName] is JsonValue value
                && value.TryGetValue(out string? stringValue)
                && !string.IsNullOrWhiteSpace(stringValue)
                    ? stringValue.Trim()
                    : fallback;
        }

        private static string ReadRefreshMinutes(JsonObject metadata)
        {
            if (metadata["refreshAllIntervalSeconds"] is JsonValue secondsValue
                && secondsValue.TryGetValue(out int seconds)
                && seconds > 0)
            {
                return Math.Max(1, seconds / 60).ToString();
            }

            if (metadata["refreshAllIntervalMs"] is JsonValue millisecondsValue
                && millisecondsValue.TryGetValue(out int milliseconds)
                && milliseconds > 0)
            {
                return Math.Max(1, milliseconds / 60000).ToString();
            }

            return Math.Max(1, DefaultRefreshAllIntervalSeconds / 60).ToString();
        }

        private static int ParsePositiveIntOrDefault(string value, int fallback)
        {
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
        }
    }

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